using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace TransparentWinUI3
{
    /// <summary>
    /// LibRaw 辅助类，用于处理相机 RAW 格式图片
    /// </summary>
    public class LibRawHelper
    {
        private readonly string _dcrawEmuPath;
        private readonly string _librawDllPath;
        private readonly string _librawDllDirectory;
        
        // Expose path for other services if needed, or keeping it private is fine.

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool AddDllDirectory(string NewDirectory);

        public LibRawHelper()
        {
            // LibRaw 工具路径
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _librawDllDirectory = Path.Combine(baseDir, "LibRaw", "bin");
            _dcrawEmuPath = Path.Combine(_librawDllDirectory, "dcraw_emu.exe");
            _librawDllPath = Path.Combine(_librawDllDirectory, "libraw.dll");
            
            // 设置 DLL 搜索路径，帮助 Windows 找到 libraw.dll 及其依赖项
            if (Directory.Exists(_librawDllDirectory))
            {
                SetDllDirectory(_librawDllDirectory);
                Debug.WriteLine($"[LibRaw] DLL search path set to: {_librawDllDirectory}");
            }
        }

        public static bool IsRawFormat(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return false;
            ext = ext.ToLowerInvariant();
            string[] raws = { ".arw", ".cr2", ".cr3", ".nef", ".nrw", ".dng", ".orf", ".raf", ".rw2", ".pef", ".srw" };
            return Array.Exists(raws, e => e == ext);
        }

        public bool IsLibRawAvailable()
        {
            return File.Exists(_dcrawEmuPath) && File.Exists(_librawDllPath);
        }

        /// <summary>
        /// 从内存处理 RAW 文件 - 完全在内存中处理，无需临时文件
        /// </summary>
        /// <param name="rawFilePath">RAW 文件路径</param>
        /// <returns>BitmapSource 或 null</returns>
        public async Task<BitmapSource?> ProcessRawFromMemoryAsync(string rawFilePath)
        {
            // 创建详细日志
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libraw_memory_debug.log");
            void Log(string message)
            {
                try
                {
                    string logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n";
                    File.AppendAllText(logPath, logMessage);
                    Debug.WriteLine($"[LibRaw Memory] {message}");
                }
                catch { }
            }

            Log($"========== 开始内存处理 ==========");
            Log($"RAW 文件: {rawFilePath}");
            Log($"DLL 路径: {_librawDllPath}");
            Log($"日志文件: {logPath}");
            
            if (!File.Exists(rawFilePath) || !File.Exists(_librawDllPath))
            {
                Log($"文件检查失败: RAW存在={File.Exists(rawFilePath)}, DLL存在={File.Exists(_librawDllPath)}");
                Debug.WriteLine($"[LibRaw Memory] File check failed: RAW={File.Exists(rawFilePath)}, DLL={File.Exists(_librawDllPath)}");
                return null;
            }

            IntPtr libraw = IntPtr.Zero;
            IntPtr processedImage = IntPtr.Zero;

            try
            {
                // 1. 读取 RAW 文件到内存
                Log("步骤 1: 读取 RAW 文件到内存...");
                byte[] rawData = await File.ReadAllBytesAsync(rawFilePath);
                Log($"✓ 读取成功: {rawData.Length / 1024.0 / 1024.0:F2} MB");
                Debug.WriteLine($"[LibRaw Memory] Loaded RAW: {rawData.Length / 1024.0 / 1024.0:F2} MB");

                // 2. 初始化 LibRaw
                Log("步骤 2: 初始化 LibRaw...");
                Log($"DLL 完整路径: {_librawDllPath}");
                
                try
                {
                    // 手动加载 DLL
                    if (!LibRawNative.LoadLibRawDll(_librawDllPath))
                    {
                        Log("❌ 无法加载 libraw.dll");
                        Debug.WriteLine("[LibRaw Memory] Failed to load libraw.dll");
                        return null;
                    }
                    Log("✓ libraw.dll 加载成功");
                    
                    libraw = LibRawNative.libraw_init(0);
                    if (libraw == IntPtr.Zero)
                    {
                        Log("❌ libraw_init 返回 null");
                        Debug.WriteLine("[LibRaw Memory] Failed to initialize - libraw_init returned null");
                        return null;
                    }
                    Log($"✓ LibRaw 初始化成功: {libraw}");
                    Debug.WriteLine($"[LibRaw Memory] ✓ LibRaw initialized: {libraw}");
                }
                catch (DllNotFoundException ex)
                {
                    Log($"❌ DLL 未找到: {ex.Message}");
                    Debug.WriteLine($"[LibRaw Memory] DLL not found: {ex.Message}");
                    return null;
                }
                catch (Exception ex)
                {
                    Log($"❌ 初始化异常: {ex.GetType().Name} - {ex.Message}");
                    Debug.WriteLine($"[LibRaw Memory] Init exception: {ex.GetType().Name} - {ex.Message}");
                    return null;
                }

                // 3. 从内存缓冲区打开 RAW 数据
                Log("步骤 3: 从内存缓冲区打开 RAW...");
                int ret = LibRawNative.libraw_open_buffer(libraw, rawData, rawData.Length);
                if (ret != LibRawNative.LIBRAW_SUCCESS)
                {
                    Log($"❌ 打开缓冲区失败: code={ret}, msg={LibRawNative.GetErrorMessage(ret)}");
                    Debug.WriteLine($"[LibRaw Memory] Open buffer failed: code={ret}, msg={LibRawNative.GetErrorMessage(ret)}");
                    return null;
                }
                Log($"✓ 缓冲区打开成功");
                Debug.WriteLine($"[LibRaw Memory] ✓ Buffer opened");

                // 4. 解包 RAW 数据
                Log("步骤 4: 解包 RAW 数据...");
                ret = LibRawNative.libraw_unpack(libraw);
                if (ret != LibRawNative.LIBRAW_SUCCESS)
                {
                    Log($"❌ 解包失败: code={ret}, msg={LibRawNative.GetErrorMessage(ret)}");
                    Debug.WriteLine($"[LibRaw Memory] Unpack failed: code={ret}, msg={LibRawNative.GetErrorMessage(ret)}");
                    return null;
                }
                Log($"✓ 解包成功");
                Debug.WriteLine($"[LibRaw Memory] ✓ Unpacked");

                // 5. 设置输出参数（可选，跳过使用默认值）
                // 注意：此版本的 LibRaw 可能没有 libraw_get_output_params 函数
                // 我们跳过这一步，使用默认参数
                Log("步骤 5: 使用默认参数处理...");
                /*
                IntPtr paramsPtr = LibRawNative.libraw_get_output_params(libraw);
                if (paramsPtr != IntPtr.Zero)
                {
                    // use_camera_wb = 1 (offset for this field in the struct)
                    Marshal.WriteInt32(IntPtr.Add(paramsPtr, 212), 1);
                    // output_color = 1 (sRGB)
                    Marshal.WriteInt32(IntPtr.Add(paramsPtr, 220), 1);
                    Debug.WriteLine($"[LibRaw Memory] ✓ Parameters set");
                }
                */

                // 6. 处理 RAW 图像
                ret = LibRawNative.libraw_dcraw_process(libraw);
                if (ret != LibRawNative.LIBRAW_SUCCESS)
                {
                    Debug.WriteLine($"[LibRaw Memory] Process failed: code={ret}, msg={LibRawNative.GetErrorMessage(ret)}");
                    return null;
                }
                Debug.WriteLine($"[LibRaw Memory] ✓ Processed");

                // 7. 获取处理后的图像数据
                int errcode = 0;
                processedImage = LibRawNative.libraw_dcraw_make_mem_image(libraw, ref errcode);
                if (processedImage == IntPtr.Zero)
                {
                    Debug.WriteLine($"[LibRaw Memory] Make mem image failed: code={errcode}, msg={LibRawNative.GetErrorMessage(errcode)}");
                    return null;
                }
                Debug.WriteLine($"[LibRaw Memory] ✓ Memory image created");

                // 8. 读取图像信息
                var imageInfo = Marshal.PtrToStructure<LibRawNative.libraw_processed_image_t>(processedImage);
                Debug.WriteLine($"[LibRaw Memory] Image: {imageInfo.width}x{imageInfo.height}, " +
                               $"colors={imageInfo.colors}, bits={imageInfo.bits}, type={imageInfo.type}");

                // 9. 复制图像数据
                int structSize = Marshal.SizeOf<LibRawNative.libraw_processed_image_t>();
                IntPtr dataPtr = IntPtr.Add(processedImage, structSize);
                byte[] imageData = new byte[imageInfo.data_size];
                Marshal.Copy(dataPtr, imageData, 0, (int)imageInfo.data_size);
                Debug.WriteLine($"[LibRaw Memory] ✓ Image data copied: {imageInfo.data_size} bytes");

                // 10. 创建 BitmapImage
                if (imageInfo.type == LibRawNative.LibRaw_image_formats.LIBRAW_IMAGE_BITMAP)
                {
                    var bitmap = await CreateBitmapFromRgbAsync(imageData, imageInfo.width, imageInfo.height, imageInfo.colors);
                    Debug.WriteLine($"[LibRaw Memory] ✓ Bitmap created successfully");
                    return bitmap;
                }

                Debug.WriteLine($"[LibRaw Memory] Unsupported image type: {imageInfo.type}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LibRaw Memory] Exception: {ex.GetType().Name}");
                Debug.WriteLine($"[LibRaw Memory] Message: {ex.Message}");
                Debug.WriteLine($"[LibRaw Memory] Stack: {ex.StackTrace}");
                return null;
            }
            finally
            {
                if (processedImage != IntPtr.Zero)
                {
                    try { LibRawNative.libraw_dcraw_clear_mem(processedImage); }
                    catch (Exception ex) { Debug.WriteLine($"[LibRaw Memory] Clear mem failed: {ex.Message}"); }
                }
                if (libraw != IntPtr.Zero)
                {
                    try { LibRawNative.libraw_close(libraw); }
                    catch (Exception ex) { Debug.WriteLine($"[LibRaw Memory] Close failed: {ex.Message}"); }
                }
            }
        }

        /// <summary>
        /// 从 RAW 文件获取嵌入的缩略图 (快速预览)
        /// </summary>
        public async Task<BitmapSource?> GetThumbnailAsync(string rawFilePath)
        {
             // 创建详细日志
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libraw_thumb_debug.log");
            void Log(string message)
            {
                try
                {
                    string logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n";
                    File.AppendAllText(logPath, logMessage);
                    Debug.WriteLine($"[LibRaw Thumb] {message}");
                }
                catch { }
            }

            if (!File.Exists(rawFilePath) || !IsLibRawAvailable())
                return null;

            IntPtr libraw = IntPtr.Zero;
            IntPtr processedImage = IntPtr.Zero;

            try
            {
                Log($"开始提取缩略图: {rawFilePath}");

                // 1. 读取文件
                byte[] rawData = await File.ReadAllBytesAsync(rawFilePath);

                // 2. 初始化
                if (!LibRawNative.LoadLibRawDll(_librawDllPath)) return null;
                libraw = LibRawNative.libraw_init(0);
                if (libraw == IntPtr.Zero) return null;

                // 3. 打开缓冲区
                if (LibRawNative.libraw_open_buffer(libraw, rawData, rawData.Length) != LibRawNative.LIBRAW_SUCCESS)
                    return null;

                // 4. 解包缩略图 (关键步骤)
                int ret = LibRawNative.libraw_unpack_thumb(libraw);
                if (ret != LibRawNative.LIBRAW_SUCCESS)
                {
                    Log($"解包缩略图失败: {ret}");
                    return null;
                }

                // 5. 生成内存图像
                int errcode = 0;
                processedImage = LibRawNative.libraw_dcraw_make_mem_thumb(libraw, ref errcode);
                if (processedImage == IntPtr.Zero)
                {
                    Log($"生成内存缩略图失败: {errcode}");
                    return null;
                }

                // 6. 读取图像信息
                var imageInfo = Marshal.PtrToStructure<LibRawNative.libraw_processed_image_t>(processedImage);
                Log($"缩略图信息: {imageInfo.width}x{imageInfo.height}, type={imageInfo.type}, size={imageInfo.data_size}");

                // 7. 处理数据
                int structSize = Marshal.SizeOf<LibRawNative.libraw_processed_image_t>();
                IntPtr dataPtr = IntPtr.Add(processedImage, structSize);
                byte[] imageData = new byte[imageInfo.data_size];
                Marshal.Copy(dataPtr, imageData, 0, (int)imageInfo.data_size);

                // 8. 创建 Bitmap
                if (imageInfo.type == LibRawNative.LibRaw_image_formats.LIBRAW_IMAGE_JPEG)
                {
                    // JPEG 格式直接加载
                    using (var ms = new MemoryStream(imageData))
                    {
                        var bitmap = new BitmapImage();
                        await bitmap.SetSourceAsync(ms.AsRandomAccessStream());
                        Log("JPEG 缩略图创建成功");
                        return bitmap;
                    }
                }
                else if (imageInfo.type == LibRawNative.LibRaw_image_formats.LIBRAW_IMAGE_BITMAP)
                {
                   // RGB 格式需转换
                   return await CreateBitmapFromRgbAsync(imageData, imageInfo.width, imageInfo.height, imageInfo.colors);
                }

                return null;
            }
            catch (Exception ex)
            {
                Log($"异常: {ex.Message}");
                return null;
            }
            finally
            {
                if (processedImage != IntPtr.Zero) LibRawNative.libraw_dcraw_clear_mem(processedImage);
                if (libraw != IntPtr.Zero) LibRawNative.libraw_close(libraw);
            }
        }

        /// <summary>
        /// 从 RGB 数据创建 WriteableBitmap
        /// </summary>
        private Task<BitmapSource?> CreateBitmapFromRgbAsync(byte[] rgbData, ushort width, ushort height, ushort colors)
        {
            try
            {
                Debug.WriteLine($"[LibRaw Memory] Creating WriteableBitmap: {width}x{height}, channels={colors}");
                
                // 使用 WriteableBitmap，对大图片支持更好
                var bitmap = new WriteableBitmap(width, height);
                
                using (var stream = bitmap.PixelBuffer.AsStream())
                {
                    Debug.WriteLine($"[LibRaw Memory] Writing pixel data to WriteableBitmap...");
                    
                    int bytesPerPixel = 4; // WriteableBitmap 使用 BGRA 格式
                    byte[] bgraData = new byte[width * height * bytesPerPixel];
                    
                    // 转换 RGB 到 BGRA
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int srcIndex = (y * width + x) * colors;
                            int dstIndex = (y * width + x) * bytesPerPixel;
                            
                            if (colors >= 3)
                            {
                                bgraData[dstIndex + 0] = rgbData[srcIndex + 2]; // B
                                bgraData[dstIndex + 1] = rgbData[srcIndex + 1]; // G
                                bgraData[dstIndex + 2] = rgbData[srcIndex + 0]; // R
                                bgraData[dstIndex + 3] = 255; // A (不透明)
                            }
                        }
                    }
                    
                    Debug.WriteLine($"[LibRaw Memory] Writing {bgraData.Length / 1024 / 1024}MB to pixel buffer...");
                    stream.Write(bgraData, 0, bgraData.Length);
                }
                
                bitmap.Invalidate();
                Debug.WriteLine($"[LibRaw Memory] ✓ WriteableBitmap created: {bitmap.PixelWidth}x{bitmap.PixelHeight}");
                return Task.FromResult<BitmapSource?>(bitmap);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LibRaw Memory] ❌ Create bitmap failed: {ex.GetType().Name} - {ex.Message}");
                Debug.WriteLine($"[LibRaw Memory] Stack: {ex.StackTrace}");
                return Task.FromResult<BitmapSource?>(null);
            }
        }

        /// <summary>
        /// 转换 RAW 格式图片为 TIFF
        /// </summary>
        /// <param name="rawPath">RAW 文件路径</param>
        /// <param name="outputPath">输出 TIFF 文件路径</param>
        /// <returns>转换成功返回输出路径，失败返回 null</returns>
        public async Task<string?> ConvertRawToTiffAsync(string rawPath, string outputPath)
        {
            if (!File.Exists(rawPath) || !IsLibRawAvailable())
                return null;

            try
            {
                // dcraw_emu 参数:
                // -T: 输出 TIFF 格式
                // -w: 使用相机白平衡
                // -q 3: 使用高质量插值 (AHD)
                // -o 1: sRGB 色彩空间
                var processInfo = new ProcessStartInfo
                {
                    FileName = _dcrawEmuPath,
                    Arguments = $"-T -w -q 3 -o 1 \"{rawPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(rawPath)
                };

                using var process = Process.Start(processInfo);
                if (process == null)
                    return null;

                string stdout = await process.StandardOutput.ReadToEndAsync();
                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                // 输出调试信息
                if (!string.IsNullOrEmpty(stderr))
                {
                    Debug.WriteLine($"dcraw_emu stderr: {stderr}");
                }
                
                if (!string.IsNullOrEmpty(stdout))
                {
                    Debug.WriteLine($"dcraw_emu stdout: {stdout}");
                }

                // dcraw_emu 可能生成各种文件名格式
                // 例如：input.NEF -> input.NEF.tiff 或 input.NEF.ppm
                if (process.ExitCode == 0)
                {
                    // 查找可能的输出文件
                    string dir = Path.GetDirectoryName(rawPath) ?? "";
                    string baseFileName = Path.GetFileName(rawPath);
                    
                    // 可能的文件名模式
                    string[] possibleFiles = new[]
                    {
                        Path.ChangeExtension(rawPath, ".tiff"),  // input.tiff
                        Path.ChangeExtension(rawPath, ".tif"),   // input.tif
                        rawPath + ".tiff",                        // input.NEF.tiff
                        rawPath + ".tif",                         // input.NEF.tif
                        rawPath + ".ppm",                         // input.NEF.ppm
                    };
                    
                    foreach (var possibleFile in possibleFiles)
                    {
                        if (File.Exists(possibleFile))
                        {
                            Debug.WriteLine($"Found dcraw_emu output: {possibleFile}");
                            return possibleFile;
                        }
                    }
                    
                    // 如果都没找到，返回 null 让调用者处理
                    Debug.WriteLine($"dcraw_emu succeeded but no output file found");
                    return null;
                }
                
                // 转换失败
                string errorMsg = !string.IsNullOrEmpty(stderr) ? stderr : "未知错误";
                Debug.WriteLine($"dcraw_emu failed with exit code {process.ExitCode}: {errorMsg}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LibRaw conversion failed: {ex.Message}");
                return null;
            }
        }


        /// <summary>
        /// 获取 RAW 文件信息
        /// </summary>
        public async Task<RawImageInfo?> GetRawInfoAsync(string rawPath)
        {
            if (!File.Exists(rawPath) || !IsLibRawAvailable())
                return null;

            try
            {
                // 使用 dcraw_emu -i 获取图片信息
                var processInfo = new ProcessStartInfo
                {
                    FileName = _dcrawEmuPath,
                    Arguments = $"-i -v \"{rawPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process == null)
                    return null;

                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    return ParseRawInfo(output, rawPath);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private RawImageInfo ParseRawInfo(string output, string filePath)
        {
            var info = new RawImageInfo
            {
                FileName = Path.GetFileName(filePath)
            };

            // 解析输出信息
            foreach (var line in output.Split('\n'))
            {
                if (line.Contains("Image size:"))
                {
                    var parts = line.Split(':')[1].Trim().Split('x');
                    if (parts.Length == 2)
                    {
                        int.TryParse(parts[0].Trim(), out int width);
                        int.TryParse(parts[1].Trim(), out int height);
                        info.Width = width;
                        info.Height = height;
                    }
                }
                else if (line.Contains("Camera:"))
                {
                    info.Camera = line.Split(':')[1].Trim();
                }
                else if (line.Contains("ISO speed:"))
                {
                    info.ISO = line.Split(':')[1].Trim();
                }
            }

            return info;
        }

        /// <summary>
        /// 获取 RAW 文件的原始 Bayer 数据（未去马赛克）
        /// </summary>
        public async Task<(byte[]? rawData, int width, int height, LibRawNative.BayerPattern pattern, string debugLog)> GetRawBayerDataAsync(string rawFilePath)
        {
            if (!File.Exists(rawFilePath) || !IsLibRawAvailable())
                return (null, 0, 0, LibRawNative.BayerPattern.RGGB, "File not found or LibRaw missing");

            // 确保在后台线程运行
            return await Task.Run(() =>
            {
                var sbLogger = new System.Text.StringBuilder();
                void Log(string msg) { sbLogger.AppendLine(msg); Debug.WriteLine(msg); }

                IntPtr libraw = IntPtr.Zero;
                IntPtr processedImage = IntPtr.Zero;

                try
                {
                    if (!LibRawNative.LoadLibRawDll(_librawDllPath))
                        return (null, 0, 0, LibRawNative.BayerPattern.RGGB, "Failed to load DLL");

                    libraw = LibRawNative.libraw_init(0);
                    if (libraw == IntPtr.Zero) return (null, 0, 0, LibRawNative.BayerPattern.RGGB, "Init failed");

                    // Open
                    Log($"Opening file: {rawFilePath}");
                    int ret = LibRawNative.libraw_open_file(libraw, rawFilePath);
                    if (ret != LibRawNative.LIBRAW_SUCCESS) 
                    {
                        Log($"Open failed: {ret}");
                        return (null, 0, 0, LibRawNative.BayerPattern.RGGB, sbLogger.ToString());
                    }

                    // Unpack
                    Log("Unpacking...");
                    ret = LibRawNative.libraw_unpack(libraw);
                    if (ret != LibRawNative.LIBRAW_SUCCESS) 
                    {
                        Log($"Unpack failed: {ret}");
                        return (null, 0, 0, LibRawNative.BayerPattern.RGGB, sbLogger.ToString());
                    }

                    // Set params for RAW dump
                    IntPtr paramsPtr = LibRawNative.GetOutputParamsPtr(libraw);
                    if (paramsPtr != IntPtr.Zero)
                    {
                        var outputParams = Marshal.PtrToStructure<LibRawNative.libraw_output_params_t>(paramsPtr);
                        outputParams.document_mode = 1; // 关键：不进行去马赛克
                        outputParams.output_bps = 16;
                        outputParams.user_sat = 0;      
                        outputParams.gamm = new double[] { 1, 1, 1, 1, 1, 1 };
                        
                        // 写回参数
                        Marshal.StructureToPtr(outputParams, paramsPtr, false);
                        Log("Params set: document_mode=1, output_bps=16");
                    }
                    else
                    {
                        Log("Warning: Could not get output params ptr");
                    }

                    // Process (Apply params only)
                    Log("Processing (dcraw_process)...");
                    ret = LibRawNative.libraw_dcraw_process(libraw);
                    if (ret != LibRawNative.LIBRAW_SUCCESS)
                    {
                        Log($"Process failed: {ret}");
                        return (null, 0, 0, LibRawNative.BayerPattern.RGGB, sbLogger.ToString());
                    }

                    // Make Mem Image
                    int err = 0;
                    processedImage = LibRawNative.libraw_dcraw_make_mem_image(libraw, ref err);
                    if (processedImage == IntPtr.Zero)
                    {
                        Log($"MakeMemImage failed: {err}");
                        return (null, 0, 0, LibRawNative.BayerPattern.RGGB, sbLogger.ToString());
                    }

                    var imageInfo = Marshal.PtrToStructure<LibRawNative.libraw_processed_image_t>(processedImage);
                    
                    Log($"[LibRaw Bayer] RAW Info: W={imageInfo.width} H={imageInfo.height} C={imageInfo.colors} B={imageInfo.bits} Size={imageInfo.data_size}");
                    
                    // Copy Data
                    int structSize = Marshal.SizeOf<LibRawNative.libraw_processed_image_t>();
                    IntPtr dataPtr = IntPtr.Add(processedImage, structSize);
                    byte[] rawData = new byte[imageInfo.data_size];
                    Marshal.Copy(dataPtr, rawData, 0, (int)imageInfo.data_size);
                    Log($"Data copied: {rawData.Length} bytes");

                    // Analyze Data (Check for "White" issue)
                    if (rawData.Length > 0)
                    {
                        long sum = 0;
                        int max = 0;
                        int min = 65535;
                        // Sample strided to save time
                        for (int i = 0; i < rawData.Length; i += 200) // Stride 100 pixels (200 bytes)
                        {
                            if (i + 1 < rawData.Length)
                            {
                                ushort val = (ushort)(rawData[i] | (rawData[i + 1] << 8));
                                sum += val;
                                if (val > max) max = val;
                                if (val < min) min = val;
                            }
                        }
                        Log($"[Pixel Stats] Min: {min}, Max: {max}, Sampled Avg: {sum / (rawData.Length / 200 + 1)}");
                    }

                    // Get Bayer Pattern via Heuristic
                    uint patternVal = LibRawNative.GetBayerPattern(libraw);
                    var pattern = (LibRawNative.BayerPattern)patternVal;
                    if (pattern == 0) pattern = LibRawNative.BayerPattern.RGGB; 
                    Log($"Bayer Pattern Detected: {pattern} (0x{patternVal:X})");

                    return (rawData, (int)imageInfo.width, (int)imageInfo.height, pattern, sbLogger.ToString());
                }
                catch (Exception ex)
                {
                    Log($"Exception: {ex.Message}");
                    return (null, 0, 0, LibRawNative.BayerPattern.RGGB, sbLogger.ToString());
                }
                finally
                {
                    if (processedImage != IntPtr.Zero) LibRawNative.libraw_dcraw_clear_mem(processedImage);
                    if (libraw != IntPtr.Zero) LibRawNative.libraw_close(libraw);
                }
            });
        }

        /// <summary>
        /// 从 RAW 文件获取色彩配置 (不生成临时文件)
        /// </summary>
        public async Task<(byte[]? profile, string? description, bool isHdrPotential)> GetRawColorProfileAsync(string rawPath)
        {
            if (!File.Exists(rawPath) || !IsLibRawAvailable()) return (null, null, true);

            return await Task.Run(async () =>
            {
                IntPtr libraw = IntPtr.Zero;
                IntPtr processedThumb = IntPtr.Zero;
                try
                {
                    if (!LibRawNative.LoadLibRawDll(_librawDllPath)) return (null, null, true);
                    libraw = LibRawNative.libraw_init(0);
                    if (libraw == IntPtr.Zero) return (null, null, true);

                    // 1. Open file (using Unicode version for Windows)
                    if (LibRawNative.libraw_open_filew(libraw, rawPath) != LibRawNative.LIBRAW_SUCCESS)
                        return (null, null, true);

                    // 2. Try direct profile extraction from RAW metadata (Best & Longest path)
                    // ... (keeping implementation)
                    
                    // 3. Unpack Thumbnail in memory
                    if (LibRawNative.libraw_unpack_thumb(libraw) == LibRawNative.LIBRAW_SUCCESS)
                    {
                        int err = 0;
                        processedThumb = LibRawNative.libraw_dcraw_make_mem_thumb(libraw, ref err);
                        if (processedThumb != IntPtr.Zero)
                        {
                            var info = Marshal.PtrToStructure<LibRawNative.libraw_processed_image_t>(processedThumb);
                            if (info.type == LibRawNative.LibRaw_image_formats.LIBRAW_IMAGE_JPEG && info.data_size > 0)
                            {
                                int structSize = Marshal.SizeOf<LibRawNative.libraw_processed_image_t>();
                                IntPtr dataPtr = IntPtr.Add(processedThumb, structSize);
                                byte[] jpegBytes = new byte[info.data_size];
                                Marshal.Copy(dataPtr, jpegBytes, 0, (int)info.data_size);

                                // Use WIC on the memory stream to extract ICC
                                using (var ms = new MemoryStream(jpegBytes))
                                {
                                    try 
                                    {
                                        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
                                        var frame = await decoder.GetFrameAsync(0);
                                        
                                        // Standard JPEG ICC path
                                        var query = "/app2/icc";
                                        var props = await frame.BitmapProperties.GetPropertiesAsync(new[] { query });
                                        if (props.TryGetValue(query, out var val) && val.Value is byte[] icc)
                                        {
                                            System.Diagnostics.Debug.WriteLine($"[LibRaw] Found ICC in memory thumbnail: {icc.Length} bytes");
                                            return (icc, "Embedded in RAW Thumbnail", true);
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                    
                    // 4. Heuristic: If it's a DNG, it might have specific DNG color matrixes we could convert,
                    // but standard ICC extraction happens in the WIC layer for DNG usually.
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LibRaw Profile] Error: {ex.Message}");
                }
                finally
                {
                    if (processedThumb != IntPtr.Zero) LibRawNative.libraw_dcraw_clear_mem(processedThumb);
                    if (libraw != IntPtr.Zero) LibRawNative.libraw_close(libraw);
                }

                return (null, null, true);
            });
        }
    }

    public class RawImageInfo
    {
        public string FileName { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public string Camera { get; set; } = "未知";
        public string ISO { get; set; } = "未知";

        public override string ToString()
        {
            return $"📷 RAW 图片\n" +
                   $"文件: {FileName}\n" +
                   $"尺寸: {Width} x {Height}\n" +
                   $"相机: {Camera}\n" +
                   $"ISO: {ISO}";
        }
    }
}

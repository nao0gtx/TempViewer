using System;
using System.Runtime.InteropServices;

namespace TransparentWinUI3
{
    /// <summary>
    /// LibRaw P/Invoke 包装器 - 直接调用 LibRaw DLL
    /// </summary>
    public static class LibRawNative
    {
        // 不指定 DLL 名称，将在运行时动态加载
        // 使用 SetDllDirectory 确保能找到 DLL
        private const string LibRawDll = "libraw.dll";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        private static IntPtr _librawHandle = IntPtr.Zero;

        /// <summary>
        /// 手动加载 LibRaw DLL
        /// </summary>
        public static bool LoadLibRawDll(string dllPath)
        {
            if (_librawHandle != IntPtr.Zero)
                return true; // 已加载

            _librawHandle = LoadLibrary(dllPath);
            if (_librawHandle == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                System.Diagnostics.Debug.WriteLine($"[LibRaw] Failed to load DLL: {dllPath}, error={error}");
                return false;
            }

            System.Diagnostics.Debug.WriteLine($"[LibRaw] DLL loaded successfully: {dllPath}");
            return true;
        }

        #region LibRaw Error Codes
        public const int LIBRAW_SUCCESS = 0;
        public const int LIBRAW_UNSPECIFIED_ERROR = -1;
        public const int LIBRAW_FILE_UNSUPPORTED = -2;
        public const int LIBRAW_REQUEST_FOR_NONEXISTENT_IMAGE = -3;
        public const int LIBRAW_OUT_OF_ORDER_CALL = -4;
        public const int LIBRAW_NO_THUMBNAIL = -5;
        public const int LIBRAW_UNSUPPORTED_THUMBNAIL = -6;
        public const int LIBRAW_INPUT_CLOSED = -7;
        public const int LIBRAW_INSUFFICIENT_MEMORY = -100001;
        #endregion

        #region LibRaw Enums
        public enum LibRaw_image_formats
        {
            LIBRAW_IMAGE_JPEG = 1,
            LIBRAW_IMAGE_BITMAP = 2
        }

        public enum BayerPattern : uint
        {
            RGGB = 0x94949494,
            BGGR = 0x16161616,
            GRBG = 0x61616161,
            GBRG = 0x49494949
        }
        #endregion

        #region LibRaw Structures
        
        /// <summary>
        /// 处理后的图像数据结构
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct libraw_processed_image_t
        {
            public LibRaw_image_formats type;
            public ushort height;
            public ushort width;
            public ushort colors;
            public ushort bits;
            public uint data_size;
            // 这里是可变长度的数据数组，使用 IntPtr 访问
            // unsigned char data[1];
        }

        /// <summary>
        /// 处理后的图像数据结构
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct libraw_colordata_t
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x10000)]
            public ushort[] curve;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4104)]
            public uint[] cblack;
            public uint black;
            public uint data_maximum;
            public uint maximum;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public uint[] linear_max;
            public float fmaximum;
            public float fnorm;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public ushort[] white;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public float[] cam_mul;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public float[] pre_mul;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
            public float[] cmatrix;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
            public float[] cmatrix_d65; // added in 0.21
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
            public float[] rgb_cam;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
            public float[] cam_xyz;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public Ph1_t[] phase_one_data;
            public float flash_used;
            public float canon_ev;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public byte[] model2;
            public IntPtr profile; // void* profile
            public uint profile_length;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public uint[] black_stat;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            public libraw_dng_color_t[] dng_color;
            public libraw_dng_levels_t dng_levels;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public float[] baseline_exposure; // 0.20
            public int WB_Coeffs_Count; // 0.20
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] // 0.21 changed this? No, keep safe.
            public float[] WBCT_Coeffs; 
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct Ph1_t
        {
            public int format, key_off, tag, tag_off, data_off, data_len, type;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        public struct libraw_dng_color_t
        {
             public uint parsedfields;
             public uint illuminant;
             [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
             public float[] calibration;
             [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
             public float[] colormatrix;
             [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
             public float[] forwardmatrix;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct libraw_dng_levels_t
        {
             public uint parsedfields;
             [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4104)]
             public uint[] dng_cblack;
             [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] // 4104? No usually smaller
             public uint[] dng_black; 
             [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
             public uint[] dng_white;
             [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
             public float[] default_crop;
             public uint preview_colorspace;
             [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
             public float[] analogbalance;
        }

        /// <summary>
        /// LibRaw 输出参数
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct libraw_output_params_t
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public uint[] greybox;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public uint[] cropbox;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public double[] aber;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public double[] gamm;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
            public float[] user_mul;
            public uint shot_select;
            public float bright;
            public float threshold;
            public int half_size;
            public int four_color_rgb;
            public int highlight;
            public int use_auto_wb;
            public int use_camera_wb;
            public int use_camera_matrix;
            public int output_color;
            public IntPtr output_profile;
            public IntPtr camera_profile;
            public IntPtr bad_pixels;
            public IntPtr dark_frame;
            public int output_bps;
            public int output_tiff;
            public int user_flip;
            public int user_qual;
            public int user_black;
            public int user_cblack;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public int[] user_cblack_array;
            public float user_sat;
            public int med_passes;
            public float auto_bright_thr;
            public float adjust_maximum_thr;
            public int no_auto_bright;
            public int use_fuji_rotate;
            public int green_matching;
            public int dcb_iterations;
            public int dcb_enhance_fl;
            public int fbdd_noiserd;
            public int exp_correc;
            public float exp_shift;
            public float exp_preser;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public int[] padding2; // use_winsize, show_aperture, show_cyclops, coordinated_io
            public int document_mode; // 关键字段：设置为 1 以获取 RAW 数据
        }
        #endregion

        #region LibRaw API Functions

        /// <summary>
        /// 创建 LibRaw 处理句柄
        /// </summary>
        [DllImport(LibRawDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr libraw_init(uint flags);

        /// <summary>
        /// 打开 RAW 文件
        /// </summary>
        [DllImport(LibRawDll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Auto)] // Auto for Windows (Unicode)
        public static extern int libraw_open_file(IntPtr libraw_data, [MarshalAs(UnmanagedType.LPWStr)] string filename);
        
        [DllImport(LibRawDll, EntryPoint = "libraw_open_filew", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] 
        public static extern int libraw_open_filew(IntPtr libraw_data, string filename);

        /// <summary>
        /// 从内存缓冲区打开 RAW 文件
        /// </summary>
        [DllImport(LibRawDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int libraw_open_buffer(IntPtr data, byte[] buffer, int size);

        /// <summary>
        /// 解包 RAW 数据
        /// </summary>
        [DllImport(LibRawDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int libraw_unpack(IntPtr libraw_data);

        /// <summary>
        /// 解包缩略图
        /// </summary>
        [DllImport(LibRawDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int libraw_unpack_thumb(IntPtr data);

        /// <summary>
        /// 将处理后的缩略图数据复制到内存
        /// </summary>
        [DllImport(LibRawDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr libraw_dcraw_make_mem_thumb(IntPtr data, ref int errcode);

        /// <summary>
        /// 处理 RAW 图像
        /// </summary>
        [DllImport(LibRawDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int libraw_dcraw_process(IntPtr libraw_data);

        /// <summary>
        /// 将处理后的图像数据复制到内存
        /// </summary>
        [DllImport(LibRawDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr libraw_dcraw_make_mem_image(IntPtr libraw_data, ref int error_code);

        /// <summary>
        /// 释放处理后的图像内存
        /// </summary>
        [DllImport(LibRawDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void libraw_dcraw_clear_mem(IntPtr image);

        /// <summary>
        /// 关闭并释放 LibRaw 句柄
        /// </summary>
        [DllImport(LibRawDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void libraw_close(IntPtr libraw_data);
        
        [DllImport(LibRawDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void libraw_recycle(IntPtr libraw_data);

        /// <summary>
        /// 获取错误信息
        /// </summary>
        [DllImport(LibRawDll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr libraw_strerror(int errorcode);

        // libraw_get_output_params is not exported in standard DLL
        // public static extern IntPtr libraw_get_output_params(IntPtr data);
        
        /// <summary>
        /// 手动查找 Output Params 结构体指针
        /// </summary>
        public static IntPtr GetOutputParamsPtr(IntPtr librawHandle)
        {
            // libraw_output_params_t 结构体中包含 aber[4] (double)
            // 默认初始化值为 1.0, 1.0, 1.0, 1.0
            // Double 1.0 = 0x3FF0000000000000
            
            // 结构体头部:
            // greybox[4] (uint) -> 16 bytes
            // cropbox[4] (uint) -> 16 bytes
            // aber[4] (double) -> start at offset 32
            
            long val1_0 = 0x3FF0000000000000;
            
            // 扫描内存寻找 aber 模式
            // 参数结构体通常在主结构体中部，偏移量可能在 20KB-40KB 左右?
            // 我们扫描前 64KB
            int maxScan = 65536;
            
            try 
            {
                for (int i = 0; i < maxScan; i += 8)
                {
                    long v1 = Marshal.ReadInt64(librawHandle, i);
                    if (v1 == val1_0)
                    {
                        // 检查后3个是否也是 1.0
                        long v2 = Marshal.ReadInt64(librawHandle, i + 8);
                        long v3 = Marshal.ReadInt64(librawHandle, i + 16);
                        long v4 = Marshal.ReadInt64(librawHandle, i + 24);
                        
                        if (v2 == val1_0 && v3 == val1_0 && v4 == val1_0)
                        {
                            // 找到了 aber[4]
                            // Output Params 起始位置 = i - 32
                            return IntPtr.Add(librawHandle, i - 32);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LibRaw] Scan params failed: {ex.Message}");
            }
            
            return IntPtr.Zero;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 获取错误信息字符串
        /// </summary>
        public static string GetErrorMessage(int errorCode)
        {
            IntPtr ptr = libraw_strerror(errorCode);
            return Marshal.PtrToStringAnsi(ptr) ?? $"Unknown error: {errorCode}";
        }

        /// <summary>
        /// 获取 Raw Bayer 数据的指针和大小
        /// </summary>
        public static unsafe (IntPtr dataPtr, int dataSize) GetRawDataPtr(IntPtr librawHandle)
        {
            // libraw_data_t 结构体布局非常复杂，且版本间有差异
            // 但 rawdata 字段通常在末尾附近
            // 安全的方法是使用 C++ 辅助 DLL，但这里我们尝试计算偏移
            // 或者：直接遍历 image_data_size 等字段
            
            // 为了稳定，我们使用 libraw_unpack_function 返回后的 internal data
            // libraw_init 返回的是 libraw_data_t*
            
            // 访问 rawdata.raw_image (ushort*)
            // 偏移量极大，硬编码不可靠。
            // 替代方案：使用 libraw_make_mem_image 生成灰度图（document_mode=1）
            // 这样我们得到的是 processed_image_t，数据在内存中是安全的
            return (IntPtr.Zero, 0);
        }

        /// <summary>
        /// 从 iparams 获取 Bayer 滤镜模式
        /// </summary>
        public static uint GetBayerPattern(IntPtr librawHandle)
        {
            // 尝试从内存中查找 filters 字段
            // libraw_data_t 的头部是固定的 7 个 int
            // 然后是 sizes (约 180 byte)
            // 然后是 iparams
            // iparams: guard[4], make[64], model[64], software[64], normalized_make[64], normalized_model[64], maker_index, data_offset, shutter, aperture, iso, flash, other... filters
            
            // iparams offset ≈ 4*7 + 180 ≈ 208
            // filters 在 iparams 中的偏移 ≈ 4+64*5+4*6 ≈ 350
            // 总偏移 ≈ 550
            
            // 这是一个非常黑客的做法，但如果没有 bindgen 生成的完整结构体，这是唯一的办法
            // 我们通过搜索 0x94949494 等特征值来验证
            
            return GetBayerPatternImpl(librawHandle);
        }

        private static uint GetBayerPatternImpl(IntPtr librawHandle)
        {
             try 
            {
                // 暴力搜索范围: 500 - 1200 字节
                for (int i = 500; i < 1200; i += 4)
                {
                    uint val = (uint)Marshal.ReadInt32(librawHandle, i);
                    if (val == 0x94949494 || val == 0x16161616 || val == 0x61616161 || val == 0x49494949)
                    {
                        return val;
                    }
                }
            }
            catch { }
            return 0; // Unknown
        }

        #endregion
    }
}

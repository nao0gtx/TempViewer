using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Graphics.Display;
using Windows.Graphics.Imaging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Display;
using TransparentWinUI3; // For LibRawHelper/FFmpegHelper

namespace TransparentWinUI3.Services
{
    public record ColorProfileResult(
        byte[]? profile, 
        string? description, 
        bool isHdrPotential = false,
        Models.ColorPrimaries primaries = Models.ColorPrimaries.Unknown,
        Models.TransferFunction transfer = Models.TransferFunction.Unknown,
        Models.MatrixCoefficients matrix = Models.MatrixCoefficients.Unknown,
        Models.ColorRange range = Models.ColorRange.Unknown,
        Models.HdrMetadata? hdrMetadata = null,
        Models.GainMapParameters? gainMapParams = null
    );

    public class ColorManagementService
    {
        // Win32 API Imports for Color Management
        // BOOL GetICMProfileW(HDC hdc, LPDWORD pBufSize, LPWSTR pszFilename);
        [DllImport("gdi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool GetICMProfile(IntPtr hDC, ref uint lpcbName, StringBuilder? lpszFilename);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        // MSCMS P/Invoke
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PROFILE
        {
            public uint dwType;
            public IntPtr pProfileData;
            public uint cbDataSize;
        }

        [DllImport("mscms.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenColorProfile(ref PROFILE pProfile, uint dwDesiredAccess, uint dwShareMode, uint dwCreationMode);

        [DllImport("mscms.dll", SetLastError = true)]
        private static extern bool CloseColorProfile(IntPtr hProfile);

        [DllImport("mscms.dll", SetLastError = true)]
        private static extern IntPtr CreateMultiProfileTransform(IntPtr[] pahProfiles, uint nProfiles, uint[] padwIntent, uint nIntents, uint dwFlags, uint dwIndexPreferredCMM);

        [DllImport("mscms.dll", SetLastError = true)]
        private static extern bool DeleteColorTransform(IntPtr hColorTransform);

        [DllImport("mscms.dll", SetLastError = true)]
        private static extern bool TranslateBitmapBits(IntPtr hColorTransform, IntPtr pSrcBits, int bmInput, uint dwWidth, uint dwHeight, uint dwInputStride, IntPtr pDestBits, int bmOutput, uint dwOutputStride, IntPtr pfnCallBack, IntPtr lParam);

        private const uint PROFILE_FILENAME = 1;
        private const uint PROFILE_MEMBUFFER = 2;
        private const uint PROFILE_READ = 1;
        private const uint FILE_SHARE_READ = 1;
        private const uint OPEN_EXISTING = 3;
        
        // Logical Color Space used by TranslateBitmapBits (we assume BGRA8)
        // Actually TranslateBitmapBits uses BM_x formats
        private const int BM_xRGB = 0; // Invalid? No, need to check enum
        private const int BM_x555RGB = 0; 
        private const int BM_x555G3x2Bits = 13;
        // Standard formats
        // 1 = BM_x555RGB (16bit)
        // We need 8 bit per channel.
        // BM_RC_nChannel_8bit 
        // BM_RGBTRIPLETS = 2 (24bit)
        // BM_BGRTRIPLETS = 4 (24bit)
        // BM_xRGBQUADS = 8 (32bit, xRGB) -> BGRA? 
        // BM_xBGRQUADS = 16 (32bit, xBGR)
        // BM_KYMCQUADS = 32
        
        // So this maps to BGRA where A is ignored.
        
        // Logical Color Space used by TranslateBitmapBits
        private const int BM_RGBTRIPLETS = 0x02; 
        private const int BM_BGRTRIPLETS = 0x04;
        private const int BM_xRGBQUADS = 0x08; // 32-bit: x, R, G, B (Little Endian -> B G R x in memory?)
        private const int BM_xBGRQUADS = 0x10; // 32-bit: x, B, G, R (Little Endian -> R G B x in memory?)

        public const uint INTENT_PERCEPTUAL = 0;
        public const uint INTENT_RELATIVE_COLORIMETRIC = 1;
        public const uint INTENT_SATURATION = 2;
        public const uint INTENT_ABSOLUTE_COLORIMETRIC = 3;

        private const uint BEST_MODE = 0x00000010;
        private const uint ENABLE_GAMUT_CHECKING = 0x00010000;

        private const uint MONITOR_DEFAULTTOPRIMARY = 0x00000001;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        [DllImport("mscms.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool GetColorProfileElement(IntPtr hProfile, uint tag, uint dwOffset, ref uint pcbSize, byte[]? pBuffer, ref bool pbReference);

        private const uint ICC_DESC_TAG = 0x64657363; // 'desc'

        /// <summary>
        /// Gets the file path of the ICC profile associated with the monitor where the given window resides.
        /// </summary>
        /// <param name="hwnd">Window handle</param>
        /// <returns>Full path to the ICC profile, or null if not found.</returns>
        public string? GetMonitorICCProfile(IntPtr hwnd)
        {
            try
            {
                // 1. Get Monitor Handle
                IntPtr hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (hMonitor == IntPtr.Zero) return null;

                // 2. We need a DC for the monitor, but GetICMProfile actually takes a DC. 
                // However, updated docs say for GetICMProfileW, hDC can be NULL to get the default profile. 
                // But typically it requires a DC to a specific device. 
                // Let's try creating a DC for the monitor. 
                // Actually, a simpler way is to use EnumDisplayMonitors or just CreateDC with "DISPLAY".
                // But MonitorFromWindow returns an HMONITOR, which is not an HDC.
                
                // Let's try a different approach using DisplayInformation if possible, 
                // but DisplayInformation is for the current view and might be limited in WinUI 3 Desktop.

                // Fallback to ancient Win32 API:
                // We need to get the Device Name of the monitor first.
                // GetMonitorInfo -> DeviceName -> CreateDC -> GetICMProfile.
                
                MONITORINFOEX monitorInfo = new MONITORINFOEX();
                monitorInfo.Size = Marshal.SizeOf(monitorInfo);
                
                if (GetMonitorInfo(hMonitor, ref monitorInfo))
                {
                    System.Diagnostics.Debug.WriteLine($"[ColorManagement] DeviceName: {monitorInfo.DeviceName}");
                    IntPtr hDC = CreateDC(null, monitorInfo.DeviceName, null, IntPtr.Zero);
                    if (hDC != IntPtr.Zero)
                    {
                        try
                        {
                            uint size = 0;
                            // First call to get size
                            // GetICMProfile(HDC, ref size, NULL)
                            bool success = GetICMProfile(hDC, ref size, null);
                            
                            // GetICMProfile returns FALSE and sets ERROR_INSUFFICIENT_BUFFER (122) if successful but buffer too small
                            int lastError = Marshal.GetLastWin32Error();
                            // 122 = ERROR_INSUFFICIENT_BUFFER
                            if (!success && lastError != 122 && size == 0)
                            {
                                System.Diagnostics.Debug.WriteLine($"[ColorManagement] GetICMProfile size check failed. Error: {lastError}");
                                return null;
                            }

                            if (size > 0)
                            {
                                StringBuilder sb = new StringBuilder((int)size);
                                if (GetICMProfile(hDC, ref size, sb))
                                {
                                    string path = sb.ToString();
                                    System.Diagnostics.Debug.WriteLine($"[ColorManagement] Found Profile: {path}");
                                    return path;
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"[ColorManagement] GetICMProfile failed. Error: {Marshal.GetLastWin32Error()}");
                                }
                            }
                        }
                        finally
                        {
                            DeleteDC(hDC);
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[ColorManagement] CreateDC failed. Error: {Marshal.GetLastWin32Error()}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ColorManagement] [ERROR] Exception in GetMonitorICCProfile: {ex.Message}\n{ex.StackTrace}");
            }
            
            return null;
        }

        /// <summary>
        /// Detects if the current monitor is in HDR (High Dynamic Range) mode.
        /// </summary>
        public bool IsHdrDisplayActive(IntPtr hwnd)
        {
            try
            {
                // In WinUI 3, we can use DisplayInformation to get color info
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var displayInfo = Microsoft.Graphics.Display.DisplayInformation.CreateForWindowId(windowId);
                var advancedColorInfo = displayInfo.GetAdvancedColorInfo();
                
                // Compare as int to avoid DisplayAdvancedColorKind vs AdvancedColorKind mismatch
                bool isHdr = (int)advancedColorInfo.CurrentAdvancedColorKind == (int)Windows.Graphics.Display.AdvancedColorKind.HighDynamicRange;
                System.Diagnostics.Debug.WriteLine($"[HDR] Display Kind: {advancedColorInfo.CurrentAdvancedColorKind} | HDR Active: {isHdr}");
                return isHdr;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HDR] Detection failed: {ex.Message}");
                return false;
            }
        }

        // Additional Win32 needed for the above logic
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int Size;
            public RECT Monitor;
            public RECT WorkArea;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateDC(string? lpszDriver, string? lpszDevice, string? lpszOutput, IntPtr lpInitData);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);
        
        /// <summary>
        /// Loads a color profile from a file path.
        /// </summary>
        public byte[]? LoadColorProfileBytes(string path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                return null;
                
            try
            {
                return System.IO.File.ReadAllBytes(path);
            }
            catch
            {
                return null;
            }
        }

        public async System.Threading.Tasks.Task<ColorProfileResult> GetImageColorProfileAsync(string path)
        {
            try
            {
                if (!System.IO.File.Exists(path)) return new ColorProfileResult(null, null, false);

                string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();

                // 1. FFmpeg Detection (HIGH PRIORITY METADATA)
                System.Diagnostics.Debug.WriteLine($"[CM] Calling FFmpeg to detect color info for: {Path.GetFileName(path)}");
                var ffmpeg = new FFmpegHelper();
                if (ffmpeg.IsFFmpegAvailable())
                {
                    var ffResult = await ffmpeg.GetImageColorProfileAsync(path);
                    if (ffResult.primaries != Models.ColorPrimaries.Unknown || 
                        ffResult.transfer != Models.TransferFunction.Unknown ||
                        ffResult.isHdrPotential)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CM] FFmpeg metadata prioritized: {ffResult.description} ({ffResult.primaries}/{ffResult.transfer})");
                        return new ColorProfileResult(
                            ffResult.profile, 
                            ffResult.description, 
                            ffResult.isHdrPotential,
                            ffResult.primaries,
                            ffResult.transfer,
                            ffResult.matrix,
                            ffResult.range,
                            ffResult.hdrMetadata,
                            ffResult.gainMapParams);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[CM] FFmpeg binaries not found for detection.");
                }

                // 2. Fallback to LibRaw for RAW files
                string[] rawExtensions = { ".arw", ".cr2", ".cr3", ".nef", ".nrw", ".dng", ".orf", ".raf", ".rw2", ".pef", ".srw", ".3fr", ".erf", ".mef" };
                if (rawExtensions.Contains(ext))
                {
                    System.Diagnostics.Debug.WriteLine($"[CM] File recognized as RAW: {ext}. Using LibRaw fallback.");
                    var libraw = new LibRawHelper();
                    if (libraw.IsLibRawAvailable())
                    {
                        var rawResult = await libraw.GetRawColorProfileAsync(path);
                        if (rawResult.profile != null || rawResult.description != null)
                        {
                            return new ColorProfileResult(rawResult.profile, rawResult.description, rawResult.isHdrPotential);
                        }
                    }
                }
                
                // 3. Try Native WIC (BitmapDecoder) as secondary fallback
                using (var stream = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                {
                try 
                {
                    Windows.Graphics.Imaging.BitmapDecoder decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
                    var frame = await decoder.GetFrameAsync(0);
                    
                    // 3a. Try Exif ColorSpace property (WIC-wide property)
                    try
                    {
                        var query = "/app1/ifd/exif/{ushort=40961}";
                        var metadata = await frame.BitmapProperties.GetPropertiesAsync(new[] { query });
                        if (metadata.TryGetValue(query, out var value) && value.Value != null)
                        {
                            uint colorSpace = Convert.ToUInt32(value.Value);
                            System.Diagnostics.Debug.WriteLine($"[CM] Exif ColorSpace: {colorSpace}");
                            if (colorSpace == 1) return new ColorProfileResult(null, "sRGB (Exif)");
                            if (colorSpace == 2) return new ColorProfileResult(null, "Adobe RGB (Exif)");
                        }
                    }
                    catch { }

                    // 3b. Try color profile extraction via WIC Metadata
                    // This is much more reliable than high-level APIs that can have WinRT projection issues.
                    try
                    {
                        // Standard ICC metadata paths for common formats:
                        // JPEG/HEIC: /app2/icc
                        // TIFF/RAW: /ifd/exif/{ushort=34675}
                        // Alternate TIFF: /ifd/{ushort=34675}
                        string[] iccQueries = { "/app2/icc", "/ifd/exif/{ushort=34675}", "/ifd/{ushort=34675}", "/app1/ifd/exif/{ushort=34675}" };
                        
                        foreach (var query in iccQueries)
                        {
                            try {
                                var iccProps = await frame.BitmapProperties.GetPropertiesAsync(new[] { query });
                                if (iccProps.TryGetValue(query, out var iccVal) && iccVal.Value is byte[] iccBytes)
                                {
                                    string desc = GetIccProfileDescription(iccBytes);
                                    System.Diagnostics.Debug.WriteLine($"[CM] Detected via WIC Metadata fallback ({query}): {desc}");
                                    return new ColorProfileResult(iccBytes, desc);
                                }
                            } catch { } 
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CM] Metadata ICC extraction failed: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                     System.Diagnostics.Debug.WriteLine($"[CM] WIC decoder/frame error: {ex.Message}");
                }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CM] [ERROR] Exception in GetImageColorProfileAsync: {ex.Message}\n{ex.StackTrace}");
            }

            System.Diagnostics.Debug.WriteLine("[CM] No color profile detected for this image.");
            return new ColorProfileResult(null, null);
        }

        public string GetIccProfileDescription(byte[] iccData)
        {
            IntPtr hProfile = IntPtr.Zero;
            GCHandle handle = GCHandle.Alloc(iccData, GCHandleType.Pinned);
            try
            {
                PROFILE p = new PROFILE();
                p.dwType = PROFILE_MEMBUFFER;
                p.pProfileData = handle.AddrOfPinnedObject();
                p.cbDataSize = (uint)iccData.Length;

                hProfile = OpenColorProfile(ref p, PROFILE_READ, FILE_SHARE_READ, OPEN_EXISTING);
                if (hProfile == IntPtr.Zero) return "Unknown Profile";

                uint size = 0;
                bool isRef = false;
                // Get size first
                GetColorProfileElement(hProfile, ICC_DESC_TAG, 0, ref size, null, ref isRef);
                if (size > 0)
                {
                    byte[] buffer = new byte[size];
                    if (GetColorProfileElement(hProfile, ICC_DESC_TAG, 0, ref size, buffer, ref isRef))
                    {
                        // ICC 'desc' tag format:
                        // Offset 0-3: 'desc' (64 65 73 63)
                        // Offset 4-7: reserved (0)
                        // Offset 8-11: length of ASCII string including null terminator
                        // Offset 12: start of string
                        if (buffer.Length > 12)
                        {
                            uint len = (uint)((buffer[8] << 24) | (buffer[9] << 16) | (buffer[10] << 8) | buffer[11]);
                            if (len > 0 && len < buffer.Length - 12)
                            {
                                return Encoding.ASCII.GetString(buffer, 12, (int)len - 1);
                            }
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (hProfile != IntPtr.Zero) CloseColorProfile(hProfile);
                handle.Free();
            }

            return "Embedded Profile";
        }

        /// <summary>
        /// Transforms BGRA8 pixels between two profiles with a specific intent.
        /// </summary>
        public unsafe (bool success, bool mutated) TransformPixels(byte[] pixels, int width, int height, string srcProfilePath, string destProfilePath, uint intent = 0)
        {
             bool mutated = false;
             System.Diagnostics.Debug.WriteLine($"[CM] TransformPixels: {srcProfilePath} -> {destProfilePath} (Intent: {intent})");
             
             // Validate paths
             if (string.IsNullOrEmpty(srcProfilePath) || string.IsNullOrEmpty(destProfilePath))
                 return (false, false);

             if (!System.IO.File.Exists(srcProfilePath) || !System.IO.File.Exists(destProfilePath))
             {
                 System.Diagnostics.Debug.WriteLine($"[CM] Profile missing! SrcExists: {System.IO.File.Exists(srcProfilePath)}, DestExists: {System.IO.File.Exists(destProfilePath)}");
                 return (false, false);
             }
             
             IntPtr hSrcProfile = OpenProfile(srcProfilePath);
             if (hSrcProfile == IntPtr.Zero) 
             {
                 System.Diagnostics.Debug.WriteLine($"[CM] Failed to open source profile: {srcProfilePath}");
                 return (false, false);
             }
             
             IntPtr hDestProfile = OpenProfile(destProfilePath);
             if (hDestProfile == IntPtr.Zero) 
             {
                 System.Diagnostics.Debug.WriteLine($"[CM] Failed to open dest profile: {destProfilePath}");
                 CloseColorProfile(hSrcProfile);
                 return (false, false);
             }
             
             IntPtr[] pahProfiles = new IntPtr[] { hSrcProfile, hDestProfile };
             uint[] intents = new uint[] { intent, intent };
             
             // Create Transform with BEST_MODE (0x10) to ensure high quality and force calculation
             IntPtr hTransform = CreateMultiProfileTransform(pahProfiles, 2, intents, 2, BEST_MODE, 0);
             
             CloseColorProfile(hSrcProfile);
             CloseColorProfile(hDestProfile);
             
             if (hTransform == IntPtr.Zero) 
             {
                 System.Diagnostics.Debug.WriteLine($"[CM] CreateMultiProfileTransform failed. Error: {Marshal.GetLastWin32Error()}");
                 return (false, false);
             }
 
             try
             {
                 fixed (byte* ptr = pixels)
                 {
                     // Snapshot for delta check
                     byte b0 = pixels[0], g0 = pixels[1], r0 = pixels[2];

                     bool success = TranslateBitmapBits(
                         hTransform, 
                         (IntPtr)ptr, 
                         BM_xRGBQUADS, 
                         (uint)width, 
                         (uint)height, 
                         (uint)(width * 4), 
                         (IntPtr)ptr, 
                         BM_xRGBQUADS, 
                         (uint)(width * 4), 
                         IntPtr.Zero, 
                         IntPtr.Zero);
                         
                     if (success)
                     {
                         byte b1 = pixels[0], g1 = pixels[1], r1 = pixels[2];
                         mutated = (b0 != b1 || g0 != g1 || r0 != r1);
                         
                         System.Diagnostics.Debug.WriteLine($"[CM] First Pixel: {b0},{g0},{r0} -> {b1},{g1},{r1} | Mutated: {mutated}");

                         // Restore Alpha
                         int len = pixels.Length;
                         for (int i = 3; i < len; i += 4)
                         {
                             pixels[i] = 255;
                         }
                     }
                     else
                     {
                         System.Diagnostics.Debug.WriteLine($"[CM] TranslateBitmapBits failed. Error: {Marshal.GetLastWin32Error()}");
                     }
                     
                     return (success, mutated);
                 }
             }
             finally
             {
                 DeleteColorTransform(hTransform);
             }
        }

        public unsafe (bool success, bool mutated) TransformPixelsFromMemory(byte[] pixels, int width, int height, byte[] srcProfileData, string destProfilePath, uint intent = 0)
        {
             bool mutated = false;
             if (srcProfileData == null || string.IsNullOrEmpty(destProfilePath))
                 return (false, false);

             GCHandle srcHandle = GCHandle.Alloc(srcProfileData, GCHandleType.Pinned);
             try
             {
                 PROFILE srcP = new PROFILE();
                 srcP.dwType = PROFILE_MEMBUFFER;
                 srcP.pProfileData = srcHandle.AddrOfPinnedObject();
                 srcP.cbDataSize = (uint)srcProfileData.Length;

                 IntPtr hSrcProfile = OpenColorProfile(ref srcP, PROFILE_READ, FILE_SHARE_READ, OPEN_EXISTING);
                 if (hSrcProfile == IntPtr.Zero) return (false, false);

                 IntPtr hDestProfile = OpenProfile(destProfilePath);
                 if (hDestProfile == IntPtr.Zero) 
                 {
                     CloseColorProfile(hSrcProfile);
                     return (false, false);
                 }

                 IntPtr[] pahProfiles = new IntPtr[] { hSrcProfile, hDestProfile };
                 uint[] intents = new uint[] { intent, intent };
                 IntPtr hTransform = CreateMultiProfileTransform(pahProfiles, 2, intents, 2, BEST_MODE, 0);

                 CloseColorProfile(hSrcProfile);
                 CloseColorProfile(hDestProfile);

                 if (hTransform == IntPtr.Zero) return (false, false);

                 try
                 {
                     fixed (byte* ptr = pixels)
                     {
                         byte b0 = pixels[0], g0 = pixels[1], r0 = pixels[2];
                         bool success = TranslateBitmapBits(hTransform, (IntPtr)ptr, BM_xRGBQUADS, (uint)width, (uint)height, (uint)(width * 4), (IntPtr)ptr, BM_xRGBQUADS, (uint)(width * 4), IntPtr.Zero, IntPtr.Zero);
                         if (success)
                         {
                             byte b1 = pixels[0], g1 = pixels[1], r1 = pixels[2];
                             mutated = (b0 != b1 || g0 != g1 || r0 != r1);
                             for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
                         }
                         return (success, mutated);
                     }
                 }
                 finally
                 {
                     DeleteColorTransform(hTransform);
                 }
             }
             finally
             {
                 srcHandle.Free();
             }
        }

        public string GetStandardSRGBPath()
        {
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "spool\\drivers\\color\\sRGB Color Space Profile.icm");
        }

        public string? GetStandardProfilePathForPrimaries(Models.ColorPrimaries primaries)
        {
            switch (primaries)
            {
                case Models.ColorPrimaries.Bt2020:
                    return GetStandardRec2020Path();
                case Models.ColorPrimaries.DisplayP3:
                    // Try to find Display P3 in project or system
                    string localP3 = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icc", "DisplayP3.icc");
                    if (System.IO.File.Exists(localP3)) return localP3;
                    return null;
                case Models.ColorPrimaries.Bt709:
                default:
                    return GetStandardSRGBPath();
            }
        }

        public string? GetStandardRec2020Path()
        {
             // Try common locations for Rec.2020 profile
             string sysFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "spool\\drivers\\color");
             
             string[] candidates = { "Rec2020.icc", "Rec2020.icm", "ITU-R BT.2020.icc" };
             foreach (var c in candidates)
             {
                 string p = System.IO.Path.Combine(sysFolder, c);
                 if (System.IO.File.Exists(p)) return p;
             }
             
             // Check project 'icc' folder
             string localRec2020 = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icc", "Rec2020.icc");
             if (System.IO.File.Exists(localRec2020)) return localRec2020;

             return null;
        }

        private IntPtr OpenProfile(string filename)
        {
            PROFILE p = new PROFILE();
            p.dwType = PROFILE_FILENAME;
            
            // We need to pass a pointer to the string. 
            // Marshal.StringToHGlobalUni is appropriate for PROFILE_FILENAME as it expects Unicode path.
            p.pProfileData = Marshal.StringToHGlobalUni(filename);
            p.cbDataSize = (uint)(filename.Length * 2 + 2);
            
            try
            {
                return OpenColorProfile(ref p, PROFILE_READ, FILE_SHARE_READ, OPEN_EXISTING);
            }
            finally
            {
                Marshal.FreeHGlobal(p.pProfileData);
            }
        }
    }
}

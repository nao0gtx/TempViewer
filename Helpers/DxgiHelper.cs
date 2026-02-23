using System;
using System.Runtime.InteropServices;
using WinRT;

namespace TransparentWinUI3.Helpers
{
    public static class DxgiHelper
    {
        // GUID for IDXGISwapChain4 (3F585D5A-BD4A-489E-B1F4-3DBCB6452FFB)
        private static readonly Guid IID_IDXGISwapChain4 = new Guid("3F585D5A-BD4A-489E-B1F4-3DBCB6452FFB");
        private static readonly Guid IID_IDXGISwapChain3 = new Guid("94d99bdb-f1f8-4ab0-b236-7da0170edab1");

        public const int DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709 = 0;
        public const int DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709 = 10; // scRGB
        public const int DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020 = 12; // HDR10

        [ComImport]
        [Guid("3F585D5A-BD4A-489E-B1F4-3DBCB6452FFB")] // IDXGISwapChain4 (Corrected 3D -> 3F)
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGISwapChain4
        {
            // DXGIObject
            void SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
            void SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            void GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
            void GetParent(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppParent);

            // DXGIDeviceSubObject
            void GetDevice(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppDevice);

            // DXGISwapChain
            void Present(uint SyncInterval, uint Flags);
            void GetBuffer(uint Buffer, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppSurface);
            void SetFullscreenState(int Fullscreen, [MarshalAs(UnmanagedType.Interface)] object pTarget);
            void GetFullscreenState(out int pFullscreen, [MarshalAs(UnmanagedType.Interface)] out object ppTarget);
            void GetDesc(IntPtr pDesc);
            void ResizeBuffers(uint BufferCount, uint Width, uint Height, int NewFormat, uint SwapChainFlags);
            void ResizeTarget(IntPtr pNewTargetParameters);
            void GetContainingOutput([MarshalAs(UnmanagedType.Interface)] out object ppOutput);
            void GetFrameStatistics(IntPtr pStats);
            void GetLastPresentCount(out uint pLastPresentCount);
            
            // IDXGISwapChain1
            void GetDesc1(IntPtr pDesc);
            void GetFullscreenDesc1(IntPtr pDesc);
            void GetHwnd(out IntPtr pHwnd);
            void GetCoreWindow(ref Guid refiid, [MarshalAs(UnmanagedType.Interface)] out object ppUnk);
            void Present1(uint SyncInterval, uint PresentFlags, IntPtr pPresentParameters);
            void IsTemporaryMonoSupported(out int pSupported);
            void GetRestrictToOutput([MarshalAs(UnmanagedType.Interface)] out object ppRestrictToOutput);
            void SetBackgroundColor(IntPtr pColor);
            void GetBackgroundColor(IntPtr pColor);
            void SetRotation(int Rotation);
            void GetRotation(out int pRotation);

            // IDXGISwapChain2
            void SetSourceSize(uint Width, uint Height);
            void GetSourceSize(out uint pWidth, out uint pHeight);
            void SetMaximumFrameLatency(uint MaxLatency);
            void GetMaximumFrameLatency(out uint pMaxLatency);
            void GetFrameLatencyWaitableObject(out IntPtr hWaitableObject);
            void SetMatrixTransform(IntPtr pMatrix);
            void GetMatrixTransform(IntPtr pMatrix);

            // IDXGISwapChain3
            uint GetCurrentBackBufferIndex();
            void CheckColorSpaceSupport(int ColorSpace, out uint pColorSpaceSupport); 
            void SetColorSpace1(int ColorSpace);
            void ResizeBuffers1(uint BufferCount, uint Width, uint Height, int Format, uint SwapChainFlags, IntPtr pCreationNodeMask, [MarshalAs(UnmanagedType.IUnknown)] object ppPresentQueue);

            // IDXGISwapChain4
            void SetHDRMetaData(int Type, uint Size, IntPtr pMetaData);
        }

        public const int DXGI_HDR_METADATA_TYPE_NONE = 0;
        public const int DXGI_HDR_METADATA_TYPE_HDR10 = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct DXGI_HDR_METADATA_HDR10
        {
            public ushort RedPrimary0;   
            public ushort RedPrimary1; 
            public ushort GreenPrimary0;
            public ushort GreenPrimary1;
            public ushort BluePrimary0;
            public ushort BluePrimary1;
            public ushort WhitePoint0;
            public ushort WhitePoint1;
            public uint MaxMasteringLuminance;
            public uint MinMasteringLuminance;
            public ushort MaxContentLightLevel;
            public ushort MaxFrameAverageLightLevel;
        }

        [ComImport]
        [Guid("5F10688D-EA55-4D55-A3B0-4DDB55C0C20A")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICanvasResourceWrapperNative
        {
            [PreserveSig]
            int GetNativeResource(
                IntPtr device,
                float dpi, 
                [In] ref Guid iid, 
                out IntPtr ppResource);
        }

        [ComImport]
        [Guid("94d99bdb-f1f8-4ab0-b236-7da0170edab1")] // IDXGISwapChain3
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGISwapChain3
        {
            // DXGIObject
            void SetPrivateData(ref Guid Name, uint DataSize, IntPtr pData);
            void SetPrivateDataInterface(ref Guid Name, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
            void GetPrivateData(ref Guid Name, ref uint pDataSize, IntPtr pData);
            void GetParent(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppParent);

            // DXGIDeviceSubObject
            void GetDevice(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppDevice);

            // DXGISwapChain
            void Present(uint SyncInterval, uint Flags);
            void GetBuffer(uint Buffer, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppSurface);
            void SetFullscreenState(int Fullscreen, [MarshalAs(UnmanagedType.Interface)] object pTarget);
            void GetFullscreenState(out int pFullscreen, [MarshalAs(UnmanagedType.Interface)] out object ppTarget);
            void GetDesc(IntPtr pDesc);
            void ResizeBuffers(uint BufferCount, uint Width, uint Height, int NewFormat, uint SwapChainFlags);
            void ResizeTarget(IntPtr pNewTargetParameters);
            void GetContainingOutput([MarshalAs(UnmanagedType.Interface)] out object ppOutput);
            void GetFrameStatistics(IntPtr pStats);
            void GetLastPresentCount(out uint pLastPresentCount);
            
            // IDXGISwapChain1
            void GetDesc1(IntPtr pDesc);
            void GetFullscreenDesc1(IntPtr pDesc);
            void GetHwnd(out IntPtr pHwnd);
            void GetCoreWindow(ref Guid refiid, [MarshalAs(UnmanagedType.Interface)] out object ppUnk);
            void Present1(uint SyncInterval, uint PresentFlags, IntPtr pPresentParameters);
            void IsTemporaryMonoSupported(out int pSupported);
            void GetRestrictToOutput([MarshalAs(UnmanagedType.Interface)] out object ppRestrictToOutput);
            void SetBackgroundColor(IntPtr pColor);
            void GetBackgroundColor(IntPtr pColor);
            void SetRotation(int Rotation);
            void GetRotation(out int pRotation);

            // IDXGISwapChain2
            void SetSourceSize(uint Width, uint Height);
            void GetSourceSize(out uint pWidth, out uint pHeight);
            void SetMaximumFrameLatency(uint MaxLatency);
            void GetMaximumFrameLatency(out uint pMaxLatency);
            void GetFrameLatencyWaitableObject(out IntPtr hWaitableObject);
            void SetMatrixTransform(IntPtr pMatrix);
            void GetMatrixTransform(IntPtr pMatrix);

            // IDXGISwapChain3
            uint GetCurrentBackBufferIndex();
            void CheckColorSpaceSupport(int ColorSpace, out uint pColorSpaceSupport); 
            void SetColorSpace1(int ColorSpace);
            void ResizeBuffers1(uint BufferCount, uint Width, uint Height, int Format, uint SwapChainFlags, IntPtr pCreationNodeMask, [MarshalAs(UnmanagedType.IUnknown)] object ppPresentQueue);
        }

        private static T? CastTo<T>(object obj, object? device = null) where T : class
        {
            if (obj == null) return null;

            IntPtr pUnk = IntPtr.Zero;
            bool releasePUnk = false;

            try
            {
                if (obj is WinRT.IWinRTObject winrtObj)
                {
                    pUnk = winrtObj.NativeObject.ThisPtr;
                }
                else
                {
                    pUnk = Marshal.GetIUnknownForObject(obj);
                    releasePUnk = true;
                }

                if (pUnk == IntPtr.Zero) return null;

                // Strategy 1: Direct QueryInterface
                Guid iidTarget = typeof(T).GUID;
                IntPtr pInterface = IntPtr.Zero;
                int hr = Marshal.QueryInterface(pUnk, ref iidTarget, out pInterface);
                if (hr >= 0 && pInterface != IntPtr.Zero)
                {
                    try { return Marshal.GetObjectForIUnknown(pInterface) as T; }
                    finally { Marshal.Release(pInterface); }
                }

                // Strategy 2: Win2D Native Unwrap
                Guid iidWrapper = new Guid("5F10688D-EA55-4D55-A3B0-4DDB55C0C20A");
                IntPtr pWrapper = IntPtr.Zero;
                hr = Marshal.QueryInterface(pUnk, ref iidWrapper, out pWrapper);
                if (hr >= 0 && pWrapper != IntPtr.Zero)
                {
                    try
                    {
                        var wrapper = Marshal.GetObjectForIUnknown(pWrapper) as ICanvasResourceWrapperNative;
                        if (wrapper != null)
                        {
                            IntPtr pResourceIUnk = IntPtr.Zero;
                            
                            // Attempt 1: IUnknown
                            Guid iidUnk = new Guid("00000000-0000-0000-C000-000000000046");
                            int hrUnwrap = wrapper.GetNativeResource(IntPtr.Zero, 0.0f, ref iidUnk, out pResourceIUnk);

                            // Attempt 2: IDXGIResource (Win2D often requires this specific base interface)
                            if (hrUnwrap < 0 || pResourceIUnk == IntPtr.Zero)
                            {
                                Guid iidDxgiResource = new Guid("035f3ab4-482e-4e50-b41f-8a1f72836f4a");
                                hrUnwrap = wrapper.GetNativeResource(IntPtr.Zero, 0.0f, ref iidDxgiResource, out pResourceIUnk);
                            }

                            // Attempt 3: WITH Device
                            if ((hrUnwrap < 0 || pResourceIUnk == IntPtr.Zero) && device != null)
                            {
                                IntPtr pDeviceUnk = Marshal.GetIUnknownForObject(device);
                                try {
                                    hrUnwrap = wrapper.GetNativeResource(pDeviceUnk, 0.0f, ref iidUnk, out pResourceIUnk);
                                } finally { Marshal.Release(pDeviceUnk); }
                            }

                            if (hrUnwrap >= 0 && pResourceIUnk != IntPtr.Zero)
                            {
                                try 
                                { 
                                     // Now QI for our target T (e.g. SwapChain4)
                                     IntPtr pFinalInterface = IntPtr.Zero;
                                     int hrQi = Marshal.QueryInterface(pResourceIUnk, ref iidTarget, out pFinalInterface);
                                     
                                     if (hrQi >= 0 && pFinalInterface != IntPtr.Zero)
                                     {
                                         try
                                         {
                                             T? result = Marshal.GetObjectForIUnknown(pFinalInterface) as T;
                                             if (result != null)
                                             {
                                                 System.Diagnostics.Debug.WriteLine($"[DXGI] Unwrapped {typeof(T).Name} successfully.");
                                                 return result;
                                             }
                                         }
                                         finally { Marshal.Release(pFinalInterface); }
                                     }
                                     else
                                     {
                                         System.Diagnostics.Debug.WriteLine($"[DXGI] Unwrap succeeded, but QueryInterface for {typeof(T).Name} failed. HRESULT: 0x{hrQi:X}");
                                     }
                                }
                                finally { Marshal.Release(pResourceIUnk); }
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"[DXGI] Unwrap (GetNativeResource) failed for {obj.GetType().Name}. Last HRESULT: 0x{hrUnwrap:X}");
                            }
                        }
                    }
                    finally { Marshal.Release(pWrapper); }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DXGI] CastTo Exception: {ex.Message}");
            }
            finally
            {
                if (releasePUnk && pUnk != IntPtr.Zero) Marshal.Release(pUnk);
            }

            return null;
        }

        public static void SetHDRMetaData(object swapChain, Models.HdrImageMetadata metadata, object? device = null)
        {
            try
            {
                // Must use IDXGISwapChain4
                var idxgi4 = CastTo<IDXGISwapChain4>(swapChain, device);
                if (idxgi4 != null)
                {
                    var hdr10 = new DXGI_HDR_METADATA_HDR10();
                    hdr10.RedPrimary0 = 34000; hdr10.RedPrimary1 = 16000;
                    hdr10.GreenPrimary0 = 13250; hdr10.GreenPrimary1 = 34500;
                    hdr10.BluePrimary0 = 7500; hdr10.BluePrimary1 = 3000;
                    hdr10.WhitePoint0 = 15635; hdr10.WhitePoint1 = 16450;
                    
                    hdr10.MaxMasteringLuminance = (uint)(metadata.MasteringMetadata.MaxLuminance ?? 1000);
                    hdr10.MinMasteringLuminance = (uint)((metadata.MasteringMetadata.MinLuminance ?? 0.001) * 10000);
                    hdr10.MaxContentLightLevel = (ushort)(metadata.MasteringMetadata.MaxCLL ?? 0);
                    hdr10.MaxFrameAverageLightLevel = (ushort)(metadata.MasteringMetadata.MaxFALL ?? 0);

                    int size = Marshal.SizeOf(typeof(DXGI_HDR_METADATA_HDR10));
                    IntPtr ptr = Marshal.AllocHGlobal(size);
                    try
                    {
                        Marshal.StructureToPtr(hdr10, ptr, false);
                        idxgi4.SetHDRMetaData(DXGI_HDR_METADATA_TYPE_HDR10, (uint)size, ptr);
                        System.Diagnostics.Debug.WriteLine($"[DXGI] SetHDRMetaData: MaxLum={hdr10.MaxMasteringLuminance}, MaxCLL={hdr10.MaxContentLightLevel}");
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                }
                else
                {
                    // Silent fallback
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DXGI] Error setting HDR Metadata: {ex.Message}");
            }
        }

        public static void SetColorSpace(object swapChain, int colorSpace, object? device = null)
        {
            try
            {
                // Try IDXGISwapChain4 first
                var idxgi4 = CastTo<IDXGISwapChain4>(swapChain, device);
                if (idxgi4 != null)
                {
                    idxgi4.CheckColorSpaceSupport(colorSpace, out uint support);
                    if ((support & 1) != 0) // DXGI_SWAP_CHAIN_COLOR_SPACE_SUPPORT_FLAG_PRESENT
                    {
                        idxgi4.SetColorSpace1(colorSpace);
                        System.Diagnostics.Debug.WriteLine($"[DXGI] Set ColorSpace to {colorSpace} (via SwapChain4)");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[DXGI] ColorSpace {colorSpace} NOT supported on this SwapChain4.");
                    }
                    return;
                }

                // Fallback to IDXGISwapChain3
                var idxgi3 = CastTo<IDXGISwapChain3>(swapChain, device);
                if (idxgi3 != null)
                {
                    idxgi3.CheckColorSpaceSupport(colorSpace, out uint support);
                    if ((support & 1) != 0)
                    {
                        idxgi3.SetColorSpace1(colorSpace);
                        System.Diagnostics.Debug.WriteLine($"[DXGI] Set ColorSpace to {colorSpace} (via SwapChain3)");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[DXGI] ColorSpace {colorSpace} NOT supported on this SwapChain3.");
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DXGI] Error setting ColorSpace: {ex.Message}");
            }
        }
    }
}


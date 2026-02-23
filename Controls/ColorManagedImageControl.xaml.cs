using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.Graphics.DirectX;
using Microsoft.UI;
using System.IO;
using TransparentWinUI3.Services;
using TransparentWinUI3.Models;
using TransparentWinUI3.Helpers;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Windows.Storage.Pickers;
using Windows.Storage;

namespace TransparentWinUI3.Controls
{
    public sealed partial class ColorManagedImageControl : UserControl, System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }

        private CanvasDevice _device;
        private CanvasSwapChain _swapChain;
        private string? _pendingFilePath; // Made nullable to match original declaration
        private string? _lastTempAvifPath;
        private CanvasBitmap? _sourceBitmap; // Made nullable to match original declaration
        
        // Render Target / Intermediate Buffers
        private ICanvasImage? _displayBitmap; // Made nullable to match original declaration
        private float[]? _pqTable_PQToLinear;

        private string? _sourceProfilePath; // Made nullable to match original declaration
        private string? _targetProfilePath; // Made nullable to match original declaration
        private uint _renderingIntent = ColorManagementService.INTENT_PERCEPTUAL; // Initialized here
        private ColorManagementService _cmService = new ColorManagementService();
        private TransparentWinUI3.LibRawHelper _libRawHelper = new TransparentWinUI3.LibRawHelper();
        private bool _useDiagnosticMode = false; // Initialized here
        private Models.HdrImageMetadata? _preDetectedMetadata; // To hold detection results
        private string _cmDebugInfo = "Ready";
        public string CMDebugInfo 
        { 
            get => _cmDebugInfo; 
            private set 
            {
                if (_cmDebugInfo != value)
                {
                    _cmDebugInfo = value;
                    OnPropertyChanged();
                }
            }
        }

        // Detected Profile Info
        public byte[]? DetectedSourceProfileBytes { get; private set; }
        public string? DetectedSourceProfileName { get; private set; }
        public bool IsHdrSource { get; private set; }
        public string SourcePixelFormat { get; private set; } = "Unknown";
        public float HdrIntensity { get; set; } = 1.0f;
        public bool ForceLinearGamma { get; set; } = false;
        public bool IsStrictDebugMode { get; set; } = false;

        // --- Compatibility Props (previously used by libmpv) ---
        public IntPtr ParentHwnd { get; set; } = IntPtr.Zero;


        
        // --- Refactor: Unified Decoded Image Structure ---
        private class DecodedImage
        {
            public ICanvasImage Source { get; set; }
            public HdrImageMetadata Metadata { get; set; }
            public CanvasBitmap? GainMap { get; set; } // For Branch B
            public GainMapParams? GainMapInfo { get; set; } // Params
        }

        private class GainMapParams
        {
            public float MinGain { get; set; } = 1.0f;
            public float MaxGain { get; set; } = 1.0f;
            public float Gamma { get; set; } = 1.0f;
            public float Offset { get; set; } = 0.0f; 
        }

        private DecodedImage? _decodedImage;
        private string? _lastSynthesizedPath;
        private bool _isFullscreen = false;
        // -------------------------------------------------

        public ColorManagedImageControl()
        {
            this.InitializeComponent();
        }


        private void EnsureDevice()
        {
            if (_device == null)
            {
                _device = CanvasDevice.GetSharedDevice();
                _device.DeviceLost += (s, e) => 
                {
                    _device = null;
                    _swapChain = null;
                    EnsureDevice();
                    EnsureSwapChain();
                    Render();
                };
            }
        }

        public async Task LoadImageAsync(string filePath)
        {
            _pendingFilePath = filePath;
            LoadingRing.IsActive = true;
            SwapChainPanel.Visibility = Visibility.Collapsed; // Changed from Canvas.Visibility

            // NEW: Clear previous detection state immediately
            DetectedSourceProfileBytes = null;
            DetectedSourceProfileName = null;

            // NEW: Always await profile extraction here, regardless of Canvas state.
            // This ensures the Window can reliably read the properties after this method returns.
            try 
            {
                 var profileInfo = await _cmService.GetImageColorProfileAsync(filePath);
                 DetectedSourceProfileBytes = profileInfo.profile;
                 DetectedSourceProfileName = profileInfo.description;
                 IsHdrSource = profileInfo.isHdrPotential;
                 
                 // Store detailed metadata for synchronization
                  _preDetectedMetadata = new HdrImageMetadata
                  {
                       Primaries = profileInfo.primaries,
                       Transfer = profileInfo.transfer,
                       Matrix = profileInfo.matrix,
                       Range = profileInfo.range,
                       MasteringMetadata = profileInfo.hdrMetadata ?? new Models.HdrMetadata(),
                       GainMapParams = profileInfo.gainMapParams,
                       Description = profileInfo.description ?? "Detected"
                  };
                 
                 if (DetectedSourceProfileBytes != null)
                 {
                     System.Diagnostics.Debug.WriteLine($"[CM] Detected Embedded Profile: {DetectedSourceProfileName} ({DetectedSourceProfileBytes.Length} bytes)");
                 }
                 else if (!string.IsNullOrEmpty(DetectedSourceProfileName))
                 {
                     System.Diagnostics.Debug.WriteLine($"[CM] Detected Color Space (via description): {DetectedSourceProfileName}");
                 }
                 
                 System.Diagnostics.Debug.WriteLine($"[CM] HDR Potential from metadata: {IsHdrSource}");
            }
            catch (Exception ex)
            {
                 System.Diagnostics.Debug.WriteLine($"[CM] Error extracting profile in LoadImageAsync: {ex.Message}");
            }

            // Perform loading
            await LoadResourcesAsync();
            
            // Initial Render
            Render();
        }

        public async Task ConfigureColorManagementAsync(string? sourceProfilePath, string? targetProfilePath, uint intent)
        {
            _sourceProfilePath = sourceProfilePath;
            _targetProfilePath = targetProfilePath;
            _renderingIntent = intent;

            if (_decodedImage?.Source == null) return;
            
            // Check for diagnostic mode (manual pixel manipulation to test the pipe)
            if (sourceProfilePath == "DIAGNOSTIC_RED")
            {
                _useDiagnosticMode = true;
            }
            else
            {
                _useDiagnosticMode = false;
            }

            LoadingRing.IsActive = true;
            SwapChainPanel.Visibility = Visibility.Collapsed; // Changed from Canvas.Visibility

            try
            {
                // Diagnostic mode or explicit target triggers transformation
                if (_useDiagnosticMode || !string.IsNullOrEmpty(targetProfilePath))
                {
                    await ApplyColorManagementAsync();
                }
                else
                {
                    // No target and no diagnostic -> Reset to source
                    _displayBitmap = _decodedImage?.Source ?? _sourceBitmap;
                    System.Diagnostics.Debug.WriteLine("[CM] No target specified, bypassing transformation.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CM] Error configuring CM: {ex.Message}");
            }
            finally
            {
                Render();
                LoadingRing.IsActive = false;
                if (HdrWebView.Visibility == Visibility.Visible)
                {
                    SwapChainPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    SwapChainPanel.Visibility = Visibility.Visible;
                }
            }
        }

        private async Task ApplyColorManagementAsync()
        {
            if (_decodedImage?.Source == null) return;
            if (_device == null) EnsureDevice();

            CMDebugInfo = "Processing...";
            var trace = new StringBuilder();
            trace.AppendLine($"[CM] [PIPE] File: {System.IO.Path.GetFileName(_pendingFilePath)}");
            var sourceBounds = _decodedImage.Source.GetBounds(_device);
            trace.AppendLine($"[CM] [PIPE] Size: {sourceBounds.Width}x{sourceBounds.Height}");

            try
            {
                // =========================================================
                // 1. Unified Preparation (Profiles & Targets)
                // =========================================================
                
                // Resolve Profiles (PRIORITY: Metadata/EXIF > Custom > Embedded > sRGB Default)
                Microsoft.Graphics.Canvas.Effects.ColorManagementProfile? sourceProfile = null;
                Microsoft.Graphics.Canvas.Effects.ColorManagementProfile? targetProfile = null;

                // (Logic preserved from original)
                if (_preDetectedMetadata != null && _preDetectedMetadata.Primaries != Models.ColorPrimaries.Unknown)
                {
                    string? standardPath = _cmService.GetStandardProfilePathForPrimaries(_preDetectedMetadata.Primaries);
                    if (!string.IsNullOrEmpty(standardPath) && System.IO.File.Exists(standardPath))
                    {
                        trace.AppendLine($"[CM] [PIPE] EXIF Priority: Using profile for {_preDetectedMetadata.Primaries}");
                        sourceProfile = Microsoft.Graphics.Canvas.Effects.ColorManagementProfile.CreateCustom(System.IO.File.ReadAllBytes(standardPath));
                    }
                }

                if (sourceProfile == null && _sourceProfilePath != null && System.IO.File.Exists(_sourceProfilePath))
                {
                    trace.AppendLine($"[CM] [PIPE] Source: Manual ({Path.GetFileName(_sourceProfilePath)})");
                    sourceProfile = Microsoft.Graphics.Canvas.Effects.ColorManagementProfile.CreateCustom(System.IO.File.ReadAllBytes(_sourceProfilePath));
                }

                if (sourceProfile == null && DetectedSourceProfileBytes != null)
                {
                    trace.AppendLine($"[CM] [PIPE] Source: Embedded ICC ({DetectedSourceProfileName})");
                    sourceProfile = Microsoft.Graphics.Canvas.Effects.ColorManagementProfile.CreateCustom(DetectedSourceProfileBytes);
                }

                if (sourceProfile == null)
                {
                    string srgbPath = _cmService.GetStandardSRGBPath();
                    trace.AppendLine($"[CM] [PIPE] Source: sRGB Fallback");
                    if (System.IO.File.Exists(srgbPath))
                        sourceProfile = Microsoft.Graphics.Canvas.Effects.ColorManagementProfile.CreateCustom(System.IO.File.ReadAllBytes(srgbPath));
                }

                string targetPath = _targetProfilePath ?? _cmService.GetStandardSRGBPath();
                if (System.IO.File.Exists(targetPath))
                {
                    targetProfile = Microsoft.Graphics.Canvas.Effects.ColorManagementProfile.CreateCustom(System.IO.File.ReadAllBytes(targetPath));
                }

                // =========================================================
                // 2. Setup & Tracing
                // =========================================================
                trace.AppendLine("--- Pipeline Start ---");
                
                // Determine format
                bool isInternalSynthesis = CurrentMetadata.Description?.Contains("Synthesized") == true;
                var outputFormat = (IsHdrSource || CurrentMetadata.Transfer == Models.TransferFunction.PQ || (isInternalSynthesis && CurrentMetadata.Transfer == Models.TransferFunction.Linear))
                        ? Windows.Graphics.DirectX.DirectXPixelFormat.R16G16B16A16Float 
                        : Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized;
                
                bool isHdrTarget = (outputFormat == Windows.Graphics.DirectX.DirectXPixelFormat.R16G16B16A16Float);

                // Ensure the swap chain matches the expected output format before we start rendering to it
                if (_swapChain == null || _swapChain.Format != outputFormat)
                {
                    EnsureSwapChain();
                }

                trace.AppendLine($"[Setup] Target Format: {outputFormat}, HDR Target: {isHdrTarget}");
                trace.AppendLine($"[Setup] Metadata: {CurrentMetadata.Description}, Transfer: {CurrentMetadata.Transfer}");

                // Create Render Target (The destination bitmap)
                CanvasRenderTarget renderTarget = null;
                try
                {
                     renderTarget = new CanvasRenderTarget(_device, (float)sourceBounds.Width, (float)sourceBounds.Height, 96.0f, outputFormat, CanvasAlphaMode.Premultiplied);
                }
                catch (Exception rtEx)
                {
                     trace.AppendLine($"[Setup] [ERROR] RenderTarget Creation Failed: {rtEx.Message}");
                     throw;
                }

                // 'finalImage' represents the head of the effect chain.
                // Initially it is the source bitmap.
                Microsoft.Graphics.Canvas.ICanvasImage finalImage = _decodedImage.Source;
                
                // =========================================================
                // 3. Branch Selection & Execution
                // =========================================================

                // Condition check
                // Synthesized images are Linear, we treat them as Absolute HDR for scRGB scaling
                bool isAbsoluteHdr = CurrentMetadata.Transfer == Models.TransferFunction.PQ 
                                  || CurrentMetadata.Transfer == Models.TransferFunction.HLG 
                                  || CurrentMetadata.Transfer == Models.TransferFunction.Linear
                                  || IsDataPQ;
                bool isGainMapHdr = CurrentMetadata.HasGainMap || (_decodedImage.GainMap != null && !isAbsoluteHdr);
                
                trace.AppendLine($"[Branch Check] IsAbsolute={isAbsoluteHdr}, IsGainMap={isGainMapHdr}, IsHdrTarget={isHdrTarget}");

                if (isAbsoluteHdr && isHdrTarget)
                {
                    // --- BRANCH A: Absolute PQ/HLG/Linear ---
                    trace.AppendLine($"[Branch A] Absolute HDR ({CurrentMetadata.Transfer})");
                    
                    if (CurrentMetadata.Transfer == Models.TransferFunction.Linear && !isInternalSynthesis && (CurrentMetadata.Description?.Contains("scRGB") == true || CurrentMetadata.Description?.Contains("Linear") == true))
                    {
                        // Normalization boost for external linear data (where 1.0 = peak)
                        float paperWhite = 80.0f; 
                        float maxNits = PqMaxNits > 0 ? PqMaxNits : 10000.0f;
                        float boost = maxNits / paperWhite; 
                        
                        var scaleEffect = new Microsoft.Graphics.Canvas.Effects.ColorMatrixEffect
                        {
                            Source = finalImage,
                            ColorMatrix = new Microsoft.Graphics.Canvas.Effects.Matrix5x4 { M11 = boost, M22 = boost, M33 = boost, M44 = 1.0f },
                            ClampOutput = false,
                            BufferPrecision = Microsoft.Graphics.Canvas.CanvasBufferPrecision.Precision16Float
                        };
                        finalImage = scaleEffect;
                        trace.AppendLine($"[A] HDR scRGB Scale (x{boost}) Applied to Normalized Linear Data");
                    }
                    else if (isInternalSynthesis)
                    {
                        // Synthesized data is already in multiples of SDR white (1.0 = 80 nits).
                        // 1. Apply SDR White Boost to match typical Windows DWM SDR brightness (200 nits = 2.5x boost).
                        // This prevents the image from looking too dark compared to the rest of the SDR desktop.
                        float boost = 2.5f; 
                        
                        var m = new Microsoft.Graphics.Canvas.Effects.Matrix5x4 { M11=1, M22=1, M33=1, M44=1 };
                        
                        // 2. We ONLY need to ensure color primaries are converted to BT.709 (scRGB).
                        // Since ColorManagementEffect clamps >1.0 values to SDR ICC limits, we use a manual matrix for DisplayP3, 
                        // which is the most common HDR source primary.
                        if (CurrentMetadata.Primaries == Models.ColorPrimaries.DisplayP3)
                        {
                            m.M11 = 1.2249f; m.M21 = -0.2247f; m.M31 = 0.0000f;
                            m.M12 = -0.0420f; m.M22 = 1.0419f; m.M32 = 0.0000f;
                            m.M13 = -0.0196f; m.M23 = -0.0786f; m.M33 = 1.0982f;
                        }
                        else if (CurrentMetadata.Primaries == Models.ColorPrimaries.Bt2020)
                        {
                            m.M11 = 1.6605f; m.M21 = -0.5876f; m.M31 = -0.0728f;
                            m.M12 = -0.1246f; m.M22 = 1.1329f; m.M32 = -0.0083f;
                            m.M13 = -0.0181f; m.M23 = -0.1006f; m.M33 = 1.1187f;
                        }
                        
                        // Apply Boost to the matrix
                        m.M11 *= boost; m.M21 *= boost; m.M31 *= boost;
                        m.M12 *= boost; m.M22 *= boost; m.M32 *= boost;
                        m.M13 *= boost; m.M23 *= boost; m.M33 *= boost;
                        
                        finalImage = new Microsoft.Graphics.Canvas.Effects.ColorMatrixEffect
                        {
                            Source = finalImage,
                            ColorMatrix = m,
                            ClampOutput = false,
                            BufferPrecision = Microsoft.Graphics.Canvas.CanvasBufferPrecision.Precision16Float
                        };
                        trace.AppendLine($"[A] Synthesized Linear: Applied Boost (x{boost}) & Primaries Matrix (Avoiding CMS Clip)");
                    }
                }
                else if (isGainMapHdr && isHdrTarget)
                {
                    // --- BRANCH B: Gain Map Synthesis ---
                    trace.AppendLine("[Branch B] Gain Map Synthesis");

                    if (_decodedImage.GainMap != null)
                    {
                        // TODO: Implement PixelShader or TableTransfer blend here.
                        // For now, fall back to base image to avoid crash until shader is ready.
                        trace.AppendLine("[Branch B] Gain Map Present but Shader pending. Showing Base Image.");
                    }
                    else
                    {
                        trace.AppendLine("[Branch B] Gain Map Metadata found but Image missing.");
                    }
                }
                else
                {
                    // --- BRANCH C: Standard SDR / Color Management (PRESERVED) ---
                    trace.AppendLine("[Branch C] Standard SDR / Color Management");
                    
                    // 1. Limited Range Expansion
                    if (IsLimitedRange || CurrentMetadata.Range == Models.ColorRange.Limited)
                    {
                        float scale = 255.0f / 219.0f;
                        float offset = -(16.0f / 255.0f) * scale;
                        finalImage = new Microsoft.Graphics.Canvas.Effects.ColorMatrixEffect
                        {
                            Source = finalImage,
                            ColorMatrix = new Microsoft.Graphics.Canvas.Effects.Matrix5x4 { M11 = scale, M22 = scale, M33 = scale, M44 = 1.0f, M51 = offset, M52 = offset, M53 = offset },
                            BufferPrecision = Microsoft.Graphics.Canvas.CanvasBufferPrecision.Precision16Float
                        };
                        trace.AppendLine("[C] Expanded Limited -> Full range");
                    }

                    // 2. Standard CM
                    if (!IsCmDisabled && sourceProfile != null && targetProfile != null)
                    {
                        finalImage = new ColorManagementEffect
                        {
                            Source = finalImage,
                            SourceColorProfile = sourceProfile,
                            SourceRenderingIntent = (ColorManagementRenderingIntent)_renderingIntent,
                            OutputColorProfile = targetProfile,
                            BufferPrecision = Microsoft.Graphics.Canvas.CanvasBufferPrecision.Precision16Float
                        };
                        trace.AppendLine($"[C] CMS Applied (Intent: {(ColorManagementRenderingIntent)_renderingIntent})");
                    }
                }

                // =========================================================
                // 3. Global Output Adjustments
                // =========================================================

                // Global Intensity Boost - REMOVED per user request
                /*
                if (HdrIntensity != 1.0f)
                {
                    finalImage = new Microsoft.Graphics.Canvas.Effects.ColorMatrixEffect
                    {
                        Source = finalImage,
                        ColorMatrix = new Microsoft.Graphics.Canvas.Effects.Matrix5x4 { M11 = HdrIntensity, M22 = HdrIntensity, M33 = HdrIntensity, M44 = 1.0f },
                        BufferPrecision = Microsoft.Graphics.Canvas.CanvasBufferPrecision.Precision16Float
                    };
                    trace.AppendLine($"[Global] Intensity Boost: {HdrIntensity}x");
                }
                */

                // Final Draw
                using (var ds = renderTarget.CreateDrawingSession())
                {
                    ds.Clear(Microsoft.UI.Colors.Transparent);
                    ds.DrawImage(finalImage);
                }
                
                _displayBitmap = renderTarget;
                CMDebugInfo = trace.ToString();
            }
            catch (Exception ex)
            {
                trace.AppendLine($"[CM] [PIPE] [ERROR] Pipeline failed: {ex.Message}");
                trace.AppendLine(ex.StackTrace);
                System.Diagnostics.Debug.WriteLine(trace.ToString()); // Print full trace!
                
                CMDebugInfo = trace.ToString();
                _displayBitmap = _decodedImage.Source; // Fallback
            }
            finally
            {
                Render();
            }
        }

        // private void Canvas_CreateResources(CanvasControl sender, Microsoft.Graphics.Canvas.UI.CanvasCreateResourcesEventArgs args) { } // Removed
        // private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args) { } // Removed

        private void SwapChainPanel_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureDevice();
            EnsureSwapChain();
            Render();
        }

        private void SwapChainPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            EnsureSwapChain();
            Render();
        }

        private void SwapChainPanel_CompositionScaleChanged(SwapChainPanel sender, object args)
        {
            EnsureSwapChain();
            Render();
        }

        private void EnsureSwapChain()
        {
            if (_device == null) EnsureDevice();
            
            // Determine size
            float w = (float)SwapChainPanel.ActualWidth;
            float h = (float)SwapChainPanel.ActualHeight;
            
            // Handle DPI
            // SwapChainPanel.CompositionScaleX is usually 1.0 in WinUI 3 desktop unless checking XamlRoot.RasterizationScale
            // But we should use the panel's reported scale if available, or just XamlRoot
            // For simplicity in WinUI 3 Desktop, we can check a simple logic:
            float dpi = 96.0f; // Default
            if (XamlRoot != null) dpi = (float)(96.0f * XamlRoot.RasterizationScale);

            // Sanity check
            if (w <= 0 || h <= 0) return;

            // Format Selection:
            // Use R16G16B16A16Float (scRGB) for HDR/10-bit content OR if system is in HDR mode
            // For now, let's aggressively use F16 if IsHdrSource is IsHdrPotential
            // Note: Displaying F16 on SDR screen is fine, DWM handles it.
            bool isInternalSynthesis = CurrentMetadata != null && CurrentMetadata.Description?.Contains("Synthesized") == true;
            var format = (IsHdrSource || (CurrentMetadata != null && CurrentMetadata.Transfer == Models.TransferFunction.PQ) || (isInternalSynthesis && CurrentMetadata.Transfer == Models.TransferFunction.Linear))
                ? Windows.Graphics.DirectX.DirectXPixelFormat.R16G16B16A16Float 
                : Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized;

            // If existing swap chain matches, just resize
            if (_swapChain != null)
            {
                if (_swapChain.Format == format)
                {
                    _swapChain.ResizeBuffers(w, h, dpi);
                    // Update metadata if HDR
                    if (format == Windows.Graphics.DirectX.DirectXPixelFormat.R16G16B16A16Float && CurrentMetadata != null)
                    {
                         TransparentWinUI3.Helpers.DxgiHelper.SetHDRMetaData(_swapChain, CurrentMetadata, _device);
                    }
                    return;
                }
                // Format changed, recreate
                _swapChain = null;
            }

            // Create
            _swapChain = new CanvasSwapChain(_device, w, h, dpi, format, 2, CanvasAlphaMode.Premultiplied);
            
            // Set Color Space for HDR (scRGB)
            if (format == Windows.Graphics.DirectX.DirectXPixelFormat.R16G16B16A16Float)
            {
                 TransparentWinUI3.Helpers.DxgiHelper.SetColorSpace(_swapChain, TransparentWinUI3.Helpers.DxgiHelper.DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709, _device);
                 if (CurrentMetadata != null)
                 {
                      TransparentWinUI3.Helpers.DxgiHelper.SetHDRMetaData(_swapChain, CurrentMetadata, _device);
                 }
            }

            SwapChainPanel.SwapChain = _swapChain;
            
            System.Diagnostics.Debug.WriteLine($"[CM] [SwapChain] Created: {w}x{h} @ {dpi}dpi ({format})");
        }

        private void Render()
        {
            if (_swapChain == null || _device == null) return;
            
            using (var ds = _swapChain.CreateDrawingSession(Microsoft.UI.Colors.Black))
            {
                // Simple centering logic
                if (_displayBitmap != null)
                {
                    var bounds = _displayBitmap.GetBounds(_device);
                    
                    // Fit logic
                    float sw = (float)SwapChainPanel.ActualWidth;
                    float sh = (float)SwapChainPanel.ActualHeight;
                    
                    if (bounds.Width > 0 && bounds.Height > 0)
                    {
                         // Uniform scale
                         float scale = Math.Min(sw / (float)bounds.Width, sh / (float)bounds.Height);
                         
                         // Center
                         float dx = (sw - (float)bounds.Width * scale) / 2.0f;
                         float dy = (sh - (float)bounds.Height * scale) / 2.0f;
                         
                         ds.Transform = System.Numerics.Matrix3x2.CreateScale(scale) * System.Numerics.Matrix3x2.CreateTranslation(dx, dy);
                         ds.DrawImage(_displayBitmap);
                    }
                }
            }
            
            _swapChain.Present();
        }

        private async Task LoadResourcesAsync() // Removed CanvasControl sender, CanvasDevice device parameters
        {
            if (string.IsNullOrEmpty(_pendingFilePath)) return;
            if (_device == null) EnsureDevice();

            System.Diagnostics.Debug.WriteLine($"[CM] [RESOURCE] Loading image resources for: {_pendingFilePath}");
            
            // NEW: Reset UI state to baseline to avoid leakage from previous image types
            HdrWebView.Visibility = Visibility.Collapsed;
            HdrPromptPanel.Visibility = Visibility.Collapsed;
            OpenLabButton.Visibility = Visibility.Collapsed;
            SaveSynthesisButton.IsEnabled = false;
            _lastSynthesizedPath = null;
            WebViewIcon.Symbol = Symbol.Globe;
            FullscreenIcon.Symbol = Symbol.FullScreen;
            HdrStatusText.Text = "";
            _sourceBitmap = null;
            _displayBitmap = null;
            _decodedImage = null;

            try
            {
                // Reset Metadata
                // Reset Metadata but preserve pre-detected results if available
                if (_preDetectedMetadata != null)
                {
                    CurrentMetadata = _preDetectedMetadata;
                    System.Diagnostics.Debug.WriteLine($"[CM] [RESOURCE] Synchronized metadata: {CurrentMetadata.Description} ({CurrentMetadata.Primaries}/{CurrentMetadata.Transfer})");
                }
                else
                {
                    CurrentMetadata = new Models.HdrImageMetadata { Description = "Default" };
                }
                
                // Check if it's an HDR-likely format (RAW, EXR, HDR, etc.)
                string ext = System.IO.Path.GetExtension(_pendingFilePath).ToLowerInvariant();
                bool isHdrCandidate = ext == ".hdr" || ext == ".exr" || ext == ".hif" || ext == ".avif" || ext == ".heic" || ext == ".heif" || ext == ".jpg" || ext == ".jpeg";
                bool isRaw = TransparentWinUI3.LibRawHelper.IsRawFormat(ext);
                
                // Load Bitmap with Format Awareness
                // CanvasBitmap.LoadAsync defaults to 8-bit for many formats (including HDR AVIF/HEIF).
                // We must use BitmapDecoder to explicitly request Rgba16Float.
                
                bool usedWebView2 = false;
                string webViewSourcePath = _pendingFilePath;
                string? tempAvifPath = null;

                if (isHdrCandidate && IsHdrSource)
                {
                    System.Diagnostics.Debug.WriteLine($"[CM] [RESOURCE] Detected HDR Candidate: {ext}. Routing...");
                    
                    try
                    {
                        // User Request: ONLY HEIC/HEIF (Samsung/Apple) should trigger the synthesis lab.
                        // JPEG HDR should go directly to WebView2.
                        if (ext == ".heic" || ext == ".heif")
                        {
                            System.Diagnostics.Debug.WriteLine($"[CM] [RESOURCE] HEIC HDR detected. Showing Lab option.");
                            SwapChainPanel.Visibility = Visibility.Visible;
                            OpenLabButton.Visibility = Visibility.Visible; // Show Lab trigger button
                            HdrStatusText.Text = "HEIC HDR detected. Click 'HDR Synthesis Lab' to adjust.";
                            
                            // Trigger one-time metadata analysis in background (don't block)
                            _ = Task.Run(async () => { await AnalyzeHdrMetadataAsync(_pendingFilePath); });
                        }
                        else
                        {
                            // JPEG, AVIF, HDR, EXR -> Send to WebView2 directly
                            System.Diagnostics.Debug.WriteLine($"[CM] [RESOURCE] Standard HDR format {ext}. Routing to WebView2.");
                            await LoadHdrWebViewAsync(_pendingFilePath);
                            usedWebView2 = true;
                            OpenLabButton.Visibility = Visibility.Visible;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CM] [RESOURCE] HDR Routing failed: {ex.Message}");
                    }
                }
                else if (isHdrCandidate)
                {
                    // If it's a potential candidate (HEIC/JPG) but not detected as HDR
                    // Show the lab button anyway as a tool
                    OpenLabButton.Visibility = Visibility.Visible;
                }
                
                // Fallback to standard Win2D load if WebView2 was not used or failed
                if (!usedWebView2 && isHdrCandidate)
                {
                    System.Diagnostics.Debug.WriteLine($"[CM] [RESOURCE] Loading HDR candidate directly via Win2D...");
                    HdrWebView.Visibility = Visibility.Collapsed;
                    try 
                    {
                        _sourceBitmap = await CanvasBitmap.LoadAsync(_device, _pendingFilePath);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CM] [RESOURCE] Standard load failed: {ex.Message}");
                    }
                }
                
                if (!usedWebView2 && isRaw && _sourceBitmap == null)
                {
                    System.Diagnostics.Debug.WriteLine("[CM] [RESOURCE] Detected RAW format. Using LibRaw fallback.");
                    HdrWebView.Visibility = Visibility.Collapsed;
                    var rawResult = await _libRawHelper.GetRawBayerDataAsync(_pendingFilePath);
                    if (rawResult.rawData != null)
                    {
                        // Populating Metadata for RAW
                        CurrentMetadata.Primaries = Models.ColorPrimaries.Bt2020; 
                        CurrentMetadata.Transfer = Models.TransferFunction.Linear; 
                        CurrentMetadata.Description = "RAW (Linear)";
                        CurrentMetadata.Matrix = Models.MatrixCoefficients.Identity; 

                        _sourceBitmap = CanvasBitmap.CreateFromBytes(
                            _device,
                            rawResult.rawData,
                            rawResult.width,
                            rawResult.height,
                            Windows.Graphics.DirectX.DirectXPixelFormat.R16G16B16A16UIntNormalized,
                            96.0f);
                    }
                }

                if (!usedWebView2 && _sourceBitmap == null)
                {
                    HdrWebView.Visibility = Visibility.Collapsed;
                    _sourceBitmap = await CanvasBitmap.LoadAsync(_device, _pendingFilePath);
                    
                    // Branch B (Degradation): Check for JPEG Gain Map (Ultra HDR)
                    if (ext == ".jpg" || ext == ".jpeg")
                    {
                         CurrentMetadata.Description = "JPEG (Possible Ultra HDR - SDR Base)";
                         // CurrentMetadata.HasGainMap = true; // Not set yet as synthesis isn't implemented
                    }
                }

                if (_sourceBitmap != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[CM] [RESOURCE] Successfully loaded bitmap: {_sourceBitmap.SizeInPixels.Width}x{_sourceBitmap.SizeInPixels.Height} Format: {_sourceBitmap.Format}");
                
                    // -------------------------------------------------------------
                    // Refactor: Populate DecodedImage structure
                    // -------------------------------------------------------------
                    _decodedImage = new DecodedImage
                    {
                        Source = _sourceBitmap,
                        Metadata = CurrentMetadata,
                        GainMap = null, // Future: Load from sidecar or hidden item
                        GainMapInfo = null
                    };

                    _displayBitmap = _sourceBitmap;
                    SourcePixelFormat = _sourceBitmap.Format.ToString();
                    
                    // Re-evaluate HDR status based on actual bitmap format + metadata
                    // Note: Metadata was already set in LoadImageAsync (IsHdrSource)
                    if (_sourceBitmap.Format == Windows.Graphics.DirectX.DirectXPixelFormat.R16G16B16A16Float)
                    {
                        IsHdrSource = true;
                    }
                    
                    // Update DecodedImage gain map info if metadata suggests it (placeholder)
                    if (CurrentMetadata.HasGainMap)
                    {
                        // TODO: Actually load the gain map bitmap if possible here
                        // For now we just set the flag.
                    }

                    // Ensure SwapChain matches the new content need
                    EnsureSwapChain();
                }
                if (usedWebView2)
                {
                     // Even if _sourceBitmap is null, WebView2 is handling the display
                     // Set SourcePixelFormat for debugging
                     SourcePixelFormat = "WebView2 Native HDR Engine";
                     CMDebugInfo = "Native HDR (Browser Engine)";
                     
                     // Create a dummy decodedImage so pipeline doesn't crash on null
                     _decodedImage = new DecodedImage
                     {
                         Source = null!, // We won't use Win2D Source
                         Metadata = CurrentMetadata,
                         GainMap = null,
                         GainMapInfo = null
                     };
                }
                else
                {
                    // If not using WebView2, ensure it's cleared to release resources
                    if (HdrWebView.CoreWebView2 != null)
                    {
                        HdrWebView.NavigateToString("about:blank");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CM] [RESOURCE] [ERROR] Failed to load image resources: {ex.Message}\n{ex.StackTrace}");
            }
            finally
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    LoadingRing.IsActive = false;
                    // Visibility is handled inside the loading branch to avoid flickering
                });
            }
        }

        // private void UpdateEffect() // Removed as ColorManagementEffect is no longer used for CPU CM
        // {
        //     if (_sourceBitmap == null) return;

        //     // Dispose previous effect
        //     _effect?.Dispose();

        //     _effect = new ColorManagementEffect
        //     {
        //         Source = _sourceBitmap
        //     };

        //     // If we have an explicit output profile, set it.
        //     // If null, ColorManagementEffect output is sRGB (usually).
        //     /*
        //     if (_outputProfile != null)
        //     {
        //         _effect.OutputColorProfile = _outputProfile;
        //     }
        //     */
            
        //     // We can also set SourceColorProfile if we want to override it.
        //     // _effect.SourceColorProfile = ...
        // }

        public bool ToneMapEnabled { get; set; } = true;

        private float[] GenerateAcesTable(int size, float inputScale)
        {
            // Input 'x' in table is 0..1.
            // But this 0..1 actually represents 0..inputScale (e.g. 0..10.0 linear nits normalized).
            float[] table = new float[size];
            for (int i = 0; i < size; i++)
            {
                // De-normalize input (texture value 0..1 -> linear light 0..10)
                float x = ((float)i / (size - 1)) * inputScale;

                // Narkowicz ACES
                float a = 2.51f;
                float b = 0.03f;
                float c = 2.43f;
                float d = 0.59f;
                float e = 0.14f;
                float output = (x * (a * x + b)) / (x * (c * x + d) + e);
                
                // Output of ACES is 0..1 (approx). 
                // We store this directly in the LUT.
                // Later we scale it up for HDR.
                table[i] = Math.Clamp(output, 0.0f, 1.0f); 
            }
            return table;
        }

        public bool IsDataPQ { get; set; } = false;
        
        public bool IsLimitedRange { get; set; } = false;
        
        public bool IsCmDisabled { get; set; } = false;
        
        public Models.HdrImageMetadata CurrentMetadata { get; set; } = new Models.HdrImageMetadata();
        
        // Default 10000 nits (Full range)
        public float PqMaxNits { get; set; } = 10000.0f;

        private float[] GeneratePqTable(int size)
        {
            // ST.2084 (PQ) Constants
            const float m1 = 0.1593017578125f; // 2610.0 / 16384.0
            const float m2 = 78.84375f;        // 2523.0 / 32.0
            const float c1 = 0.8359375f;       // 3424.0 / 4096.0
            const float c2 = 18.8515625f;      // 2413.0 / 128.0
            const float c3 = 18.6875f;         // 2392.0 / 128.0

            float[] table = new float[size];
            for (int i = 0; i < size; i++)
            {
                float V = (float)i / (size - 1); // Input 0..1 (PQ Code Value)

                // ST 2084 EOTF: 
                // L = ( max( V^(1/m2) - c1 , 0 ) / ( c2 - c3 * V^(1/m2) ) ) ^ (1/m1)
                
                float V_pow = MathF.Pow(V, 1.0f / m2);
                float num = Math.Max(V_pow - c1, 0.0f);
                float den = Math.Max(c2 - c3 * V_pow, 0.000001f); // Protect against divide by zero (though c2 > c3)
                
                float L = MathF.Pow(num / den, 1.0f / m1); // 0..1 (Normalized 0..10000 nits)
                
            // Scale to scRGB (1.0 = 80 nits)
            // We Output 0..1 (Normalized) to avoid "ArgumentException" in TableTransfer.
            // Scaling to 125.0 is done via ColorMatrix.
            table[i] = Math.Clamp(L, 0.0f, 1.0f);
        }
        return table;
    }

    private float[] GenerateLinearToPqTable(int size)
    {
        const float m1 = 0.1593017578125f; // 2610.0 / 16384.0
        const float m2 = 78.84375f;        // 2523.0 / 32.0
        const float c1 = 0.8359375f;       // 3424.0 / 4096.0
        const float c2 = 18.8515625f;      // 2413.0 / 128.0
        const float c3 = 18.6875f;         // 2392.0 / 128.0

        float[] table = new float[size];
        for (int i = 0; i < size; i++)
        {
            float L = (float)i / (size - 1); // Input 0..1 (representing 0..10000 nits)
            float L_m1 = MathF.Pow(L, m1);
            float num = c1 + c2 * L_m1;
            float den = 1.0f + c3 * L_m1;
            float N = MathF.Pow(num / den, m2);
            table[i] = Math.Clamp(N, 0.0f, 1.0f);
        }
        return table;
    }

    private float[] GenerateLinearToHlgTable(int size)
    {
        const float a = 0.17883277f;
        const float b = 0.28466892f;
        const float c = 0.55991073f;

        float[] table = new float[size];
        for (int i = 0; i < size; i++)
        {
            float L = (float)i / (size - 1); // Input 0..1 (representing 0..1000 nits)
            float V = 0.0f;
            if (L <= 1.0f / 12.0f)
            {
                V = MathF.Sqrt(3.0f * L);
            }
            else
            {
                V = a * MathF.Log(12.0f * L - b) + c;
            }
            table[i] = Math.Clamp(V, 0.0f, 1.0f);
        }
        return table;
    }

        private async Task LoadHdrWebViewAsync(string path, string? descriptionOverride = null)
        {
            try
            {
                if (HdrWebView.CoreWebView2 == null)
                {
                    await HdrWebView.EnsureCoreWebView2Async();
                    HdrWebView.DefaultBackgroundColor = Microsoft.UI.Colors.Black;
                }

                if (HdrWebView.CoreWebView2 != null)
                {
                    string dir = Path.GetDirectoryName(path) ?? "";
                    string file = Path.GetFileName(path);
                    
                    HdrWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                        "localapp", 
                        dir, 
                        Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);

                    string html = $@"
                    <!DOCTYPE html>
                    <html>
                    <head>
                        <style>
                            body {{ margin: 0; padding: 0; overflow: hidden; background-color: black; display: flex; justify-content: center; align-items: center; height: 100vh; }}
                            img {{ max-width: 100%; max-height: 100%; object-fit: contain; }}
                        </style>
                    </head>
                    <body>
                        <img src=""http://localapp/{Uri.EscapeDataString(file)}"" />
                    </body>
                    </html>";

                    HdrWebView.NavigateToString(html);
                    
                    CurrentMetadata.Description = descriptionOverride ?? "WebView2 (Native HDR Browser Engine)";
                    
                    // Hide SwapChain as WebView2 handles display
                    SwapChainPanel.Visibility = Visibility.Collapsed;
                    HdrWebView.Visibility = Visibility.Visible;
                    HdrPromptPanel.Visibility = Visibility.Collapsed;

                    System.Diagnostics.Debug.WriteLine($"[CM] [RESOURCE] WebView2 HDR playback started for {path}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CM] [RESOURCE] LoadHdrWebViewAsync failed: {ex.Message}");
            }
        }

        private async void WebViewToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_pendingFilePath)) return;

            if (HdrWebView.Visibility == Visibility.Collapsed)
            {
                // Switch to WebView
                // Use synthesized result if available, otherwise original file
                string pathToShow = (!string.IsNullOrEmpty(_lastSynthesizedPath) && File.Exists(_lastSynthesizedPath)) 
                                    ? _lastSynthesizedPath 
                                    : _pendingFilePath;

                await LoadHdrWebViewAsync(pathToShow);
                SwapChainPanel.Visibility = Visibility.Collapsed;
                HdrWebView.Visibility = Visibility.Visible;
                
                // State Persistence: Ensure Lab button visibility is re-evaluated after toggle
                string ext = Path.GetExtension(_pendingFilePath).ToLowerInvariant();
                bool shouldShowLab = ext == ".heic" || ext == ".heif" || ext == ".jpg" || ext == ".jpeg";
                OpenLabButton.Visibility = shouldShowLab ? Visibility.Visible : Visibility.Collapsed;

                WebViewIcon.Symbol = Symbol.Back;
                HdrStatusText.Text = "Viewing via WebView2 (Native HDR Mode)";
            }
            else
            {
                // Switch back to Win2D
                HdrWebView.Visibility = Visibility.Collapsed;
                SwapChainPanel.Visibility = Visibility.Visible;
                WebViewIcon.Symbol = Symbol.Globe;
                HdrStatusText.Text = "Viewing via Win2D (Color Managed Mode)";
            }
        }

        private void FullscreenImageBtn_Click(object sender, RoutedEventArgs e)
        {
            // Component-level immersive mode: Hide most overlays, keep toggle visible
            bool willBeImmersive = HdrStatusText.Visibility == Visibility.Visible;

            if (willBeImmersive)
            {
                // Enter "Fullscreen" (Immersive)
                HdrPromptPanel.Visibility = Visibility.Collapsed;
                DebugLogText.Visibility = Visibility.Collapsed;
                HdrStatusText.Visibility = Visibility.Collapsed;
                OpenLabButton.Visibility = Visibility.Collapsed;
                FullscreenIcon.Symbol = Symbol.BackToWindow;
            }
            else
            {
                // Exit "Fullscreen"
                HdrStatusText.Visibility = Visibility.Visible;
                
                // Restore Lab button logic
                string ext = Path.GetExtension(_pendingFilePath).ToLowerInvariant();
                if (ext == ".heic" || ext == ".heif" || ext == ".jpg" || ext == ".jpeg") 
                {
                    OpenLabButton.Visibility = Visibility.Visible;
                }
                
                FullscreenIcon.Symbol = Symbol.FullScreen;
            }
        }

        private async void LossyConvertButton_Click(object sender, RoutedEventArgs e)
        {
            await PerformUltraHdrSynthesisAsync(false);
        }

        private async void LosslessConvertButton_Click(object sender, RoutedEventArgs e)
        {
            await PerformUltraHdrSynthesisAsync(true);
        }


        private async Task PerformUltraHdrSynthesisAsync(bool isLossless)
        {
            if (string.IsNullOrEmpty(_pendingFilePath)) return;
            string workspace = GetWorkspacePath();
            
            try
            {
                LoadingRing.IsActive = true;
                HdrStatusText.Text = $"Starting {(isLossless ? "Lossless" : "Lossy")} Synthesis...";
                CMDebugInfo += $"\n[Lab] Workspace: {workspace}";
                
                // Clear previous session files in workspace
                foreach (var f in Directory.GetFiles(workspace)) try { File.Delete(f); } catch { }

                var ffmpeg = new FFmpegHelper();
                string inputPath = _pendingFilePath;
                
                // 1. Extraction with heif-dec.exe (Step 1)
                string baseJpg = Path.Combine(workspace, "base-s.jpg");
                string heifDecPath = ffmpeg.GetHeifDecPath();
                
                var heifProcess = new ProcessStartInfo
                {
                    FileName = heifDecPath,
                    Arguments = $"--with-aux --with-exif --with-xmp --skip-exif-offset --no-colons \"{inputPath}\" \"{baseJpg}\"",
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
                    WorkingDirectory = workspace
                };
                
                using (var p = await SafeStartProcessAsync(heifProcess, "Extraction"))
                {
                    if (p == null) return;
                    string err = await p.StandardError.ReadToEndAsync();
                    await p.WaitForExitAsync();
                    if (p.ExitCode != 0) { CMDebugInfo += $"\n[Lab] heif-dec failed: {err}"; return; }
                }

                // Identify Gain Map from auxiliary outputs
                string gmPath = "";
                var auxFiles = Directory.GetFiles(workspace, "base-s-*");
                foreach (var f in auxFiles)
                {
                    string name = Path.GetFileName(f).ToLowerInvariant();
                    if (name.Contains("gainmap") || name.Contains("aux") || name.Contains("-1"))
                    {
                        gmPath = f; break;
                    }
                }

                if (string.IsNullOrEmpty(gmPath))
                {
                    CMDebugInfo += "\n[Lab] ERROR: No Gain Map extracted.";
                    return;
                }

                // 2. Metadata: Skip parsing here, it's done once on load to avoid resetting sliders.
                // We just ensure we have the meta_all.txt for debugging if needed.
                string metaFile = Path.Combine(workspace, "meta_all.txt");
                string exifToolPath = ffmpeg.GetExifToolPath();
                if (File.Exists(exifToolPath))
                {
                    var exifProcess = new ProcessStartInfo
                    {
                        FileName = exifToolPath,
                        Arguments = $"-a -G1 -s \"{inputPath}\"",
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                        WorkingDirectory = workspace
                    };
                    using (var p = Process.Start(exifProcess))
                    {
                        if (p != null)
                        {
                            string meta = await p.StandardOutput.ReadToEndAsync();
                            await File.WriteAllTextAsync(metaFile, meta);
                            await p.WaitForExitAsync();
                        }
                    }
                }

                // 3. Lossless Path preparation (if requested)
                string finalBase = baseJpg;
                string finalGm = gmPath;

                if (isLossless)
                {
                    CMDebugInfo += "\n[Lab] Preparing Lossless Path (Y4M -> High Quality JPEG)...";
                    string baseY4m = Path.Combine(workspace, "base_lossless.y4m");
                    var y4mProcess = new ProcessStartInfo
                    {
                        FileName = heifDecPath,
                        Arguments = $"\"{inputPath}\" \"{baseY4m}\"",
                        UseShellExecute = false, CreateNoWindow = true
                    };
                    using (var p = await SafeStartProcessAsync(y4mProcess, "Y4M Extraction"))
                    {
                        if (p != null) await p.WaitForExitAsync();
                    }

                    string baseLosslessJpg = Path.Combine(workspace, "base-lossless.jpg");
                    var rawProcess = new ProcessStartInfo
                    {
                        FileName = ffmpeg.GetFFmpegPath(),
                        Arguments = $"-i \"{baseY4m}\" -q:v 1 -pix_fmt yuvj444p \"{baseLosslessJpg}\" -y", 
                        UseShellExecute = false, CreateNoWindow = true
                    };
                    using (var p = await SafeStartProcessAsync(rawProcess, "Y4M to JPEG"))
                    {
                        if (p != null) await p.WaitForExitAsync();
                    }
                    finalBase = baseLosslessJpg;
                }

                if (!finalGm.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                {
                    string gmJpg = Path.Combine(workspace, "gainmap_final.jpg");
                    await ffmpeg.ConvertImageToJpegAsync(finalGm, gmJpg, 1);
                    finalGm = gmJpg;
                }

                // 4. Construct metadata.cfg (Step 3)
                float maxBoost = (float)LabMaxBoostSlider.Value;
                float gamma = (float)LabGammaSlider.Value;
                string cfgPath = Path.Combine(workspace, "metadata.cfg");
                
                string cfgContent = 
                    $"--maxContentBoost {maxBoost:F6} {maxBoost:F6} {maxBoost:F6}\n" +
                    $"--minContentBoost 1.000000 1.000000 1.000000\n" +
                    $"--gamma {gamma:F6} {gamma:F6} {gamma:F6}\n" +
                    $"--offsetSdr 0.000000 0.000000 0.000000\n" +
                    $"--offsetHdr 0.000000 0.000000 0.000000\n" +
                    $"--hdrCapacityMin 1.000000\n" +
                    $"--hdrCapacityMax {maxBoost:F6}\n" +
                    $"--useBaseColorSpace 1\n";
                await File.WriteAllTextAsync(cfgPath, cfgContent);

                // 5. Synthesis with ultrahdr_app.exe (Step 4)
                string ultraHdrAppPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "bin", "ultrahdr_app.exe");
                string outJpg = Path.Combine(workspace, "out_uhdr.jpg");
                
                var info = await ffmpeg.GetImageInfoAsync(finalBase);
                int w = info?.Width ?? 0;
                int h = info?.Height ?? 0;

                var synthProcess = new ProcessStartInfo
                {
                    FileName = ultraHdrAppPath,
                    Arguments = $"-m 0 -i \"{finalBase}\" -g \"{finalGm}\" -f \"{cfgPath}\" -z \"{outJpg}\"",
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true,
                    WorkingDirectory = workspace
                };

                using (var p = await SafeStartProcessAsync(synthProcess, "Synthesis"))
                {
                    if (p == null) return;
                    string err = await p.StandardError.ReadToEndAsync();
                    await p.WaitForExitAsync();
                    if (p.ExitCode != 0) { CMDebugInfo += $"\n[Lab] Synthesis failed: {err}"; return; }
                }

                CMDebugInfo += $"\n[Lab] Synthesis SUCCESS: out_uhdr.jpg";
                HdrStatusText.Text = "Synthesis Complete!";

                // 6. Show Preview
                await LoadHdrWebViewAsync(outJpg);
                _lastSynthesizedPath = outJpg;

                // 7. Show Save Button (Manual)
                DispatcherQueue.TryEnqueue(() => { 
                    SaveSynthesisButton.IsEnabled = true;
                    HdrStatusText.Text = "Synthesis Complete! You can now save.";
                });
            }
            catch (Exception ex)
            {
                CMDebugInfo += $"\n[Lab] Synthesis Exception: {ex.Message}";
                HdrStatusText.Text = "Synthesis Error.";
            }
            finally { LoadingRing.IsActive = false; }
        }

        private void LabToggle_Click(object sender, RoutedEventArgs e)
        {
            SynthesisLabPanel.Visibility = (LabToggle.IsChecked == true) ? Visibility.Visible : Visibility.Collapsed;
        }

        private string GetWorkspacePath()
        {
            string appRoot = AppDomain.CurrentDomain.BaseDirectory;
            string workspace = Path.Combine(appRoot, "UltraHdr_Workspace");
            if (!Directory.Exists(workspace)) Directory.CreateDirectory(workspace);
            return workspace;
        }

        private async void ExportPqHlgButton_Click(object sender, RoutedEventArgs e)
        {
            if (_decodedImage?.Source == null) return;

            var picker = new FileSavePicker();
            IntPtr hWnd = App.WindowHandle;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeChoices.Add("HDR AVIF (PQ)", new[] { ".avif" });
            picker.FileTypeChoices.Add("HDR HEIC (PQ)", new[] { ".heic" });
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(_pendingFilePath) + "_HDR";

            StorageFile file = await picker.PickSaveFileAsync();
            if (file == null) return;

            string? tempPng = null;
            try
            {
                LoadingRing.IsActive = true;
                bool isPq = file.FileType.Equals(".avif", StringComparison.OrdinalIgnoreCase); // Simplified check
                var transfer = FFmpegHelper.HdrTransfer.PQ;

                HdrStatusText.Text = $"Encoding {transfer} OETF Transfer...";
                
                var sourceBounds = _decodedImage.Source.GetBounds(_device);
                using var renderTarget = new CanvasRenderTarget(_device, (float)sourceBounds.Width, (float)sourceBounds.Height, 96.0f, DirectXPixelFormat.R16G16B16A16UIntNormalized, CanvasAlphaMode.Premultiplied);
                using (var ds = renderTarget.CreateDrawingSession()) ds.DrawImage(_decodedImage.Source);

                tempPng = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
                using (var stream = File.OpenWrite(tempPng).AsRandomAccessStream()) await renderTarget.SaveAsync(stream, CanvasBitmapFileFormat.Png);

                var ffmpeg = new FFmpegHelper();
                await ffmpeg.TranscodeHdrToAvifAsync(tempPng, file.Path, transfer, inputIsLinear: true);

                HdrStatusText.Text = $"Export Successful: {file.Name}";
            }
            catch (Exception ex)
            {
                HdrStatusText.Text = $"Export Error: {ex.Message}";
            }
            finally
            {
                if (tempPng != null && File.Exists(tempPng)) try { File.Delete(tempPng); } catch { }
                LoadingRing.IsActive = false;
            }
        }

        private async void ExportSdrButton_Click(object sender, RoutedEventArgs e)
        {
            if (_decodedImage?.Source == null) return;

            var picker = new FileSavePicker();
            IntPtr hWnd = App.WindowHandle;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeChoices.Add("SDR JPEG", new[] { ".jpg" });
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(_pendingFilePath) + "_SDR";

            StorageFile file = await picker.PickSaveFileAsync();
            if (file == null) return;

            try
            {
                LoadingRing.IsActive = true;
                var sourceBounds = _decodedImage.Source.GetBounds(_device);
                using var renderTarget = new CanvasRenderTarget(_device, (float)sourceBounds.Width, (float)sourceBounds.Height, 96.0f);
                using (var ds = renderTarget.CreateDrawingSession()) ds.DrawImage(_decodedImage.Source);
                using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite)) await renderTarget.SaveAsync(stream, CanvasBitmapFileFormat.Jpeg);
                HdrStatusText.Text = $"Export Successful: {file.Name}";
            }
            catch (Exception ex) { HdrStatusText.Text = $"Export Error: {ex.Message}"; }
            finally { LoadingRing.IsActive = false; }
        }
        private async Task AnalyzeHdrMetadataAsync(string inputPath)
        {
            try
            {
                var ffmpeg = new FFmpegHelper();
                string exifToolPath = ffmpeg.GetExifToolPath();
                if (!File.Exists(exifToolPath)) return;

                var exifProcess = new ProcessStartInfo
                {
                    FileName = exifToolPath,
                    Arguments = $"-a -G1 -s \"{inputPath}\"",
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
                };

                using (var p = await SafeStartProcessAsync(exifProcess, "Metadata Analysis"))
                {
                    if (p == null) return;
                    string meta = await p.StandardOutput.ReadToEndAsync();
                    await p.WaitForExitAsync();

                    var lines = meta.Split('\n');
                    foreach (var line in lines)
                    {
                        if (line.Contains("GainMapMax") || line.Contains("HDRCapacityMax"))
                        {
                            var parts = line.Split(':', 2);
                            if (parts.Length == 2 && float.TryParse(parts[1].Trim(), out float val))
                            {
                                if (val > 0)
                                {
                                    DispatcherQueue.TryEnqueue(() => { LabMaxBoostSlider.Value = Math.Max(1.0f, val); });
                                    System.Diagnostics.Debug.WriteLine($"[Lab] Auto-detected MaxBoost: {val:F2}");
                                }
                            }
                        }
                    }
                    DispatcherQueue.TryEnqueue(() => { 
                        HdrStatusText.Text = "Metadata analyzed. Ready."; 
                        OpenLabButton.Opacity = 1.0;
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Lab] Metadata Analysis Error: {ex.Message}");
            }
        }

        private void LabSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (MaxBoostValueText != null && LabMaxBoostSlider != null)
                MaxBoostValueText.Text = LabMaxBoostSlider.Value.ToString("F2");
            if (GammaValueText != null && LabGammaSlider != null)
                GammaValueText.Text = LabGammaSlider.Value.ToString("F2");
        }

        private void OpenLabButton_Click(object sender, RoutedEventArgs e)
        {
            HdrPromptPanel.Visibility = (HdrPromptPanel.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
        }

        private async void SaveSynthesisButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastSynthesizedPath) || !File.Exists(_lastSynthesizedPath)) return;

            var picker = new FileSavePicker();
            IntPtr hWnd = App.WindowHandle;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeChoices.Add("Ultra HDR JPEG", new System.Collections.Generic.List<string>() { ".jpg" });
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(_pendingFilePath) + "_UltraHDR";

            StorageFile file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                try
                {
                    File.Copy(_lastSynthesizedPath, file.Path, true);
                    HdrStatusText.Text = $"Saved to: {file.Name}";
                }
                catch (Exception ex) { HdrStatusText.Text = $"Save Error: {ex.Message}"; }
            }
        }

        private void FullscreenButton_Click(object sender, RoutedEventArgs e)
        {
            var window = App.MainWindow;
            if (window == null) return;

            var appWindow = window.AppWindow;
            _isFullscreen = !_isFullscreen;
            
            if (_isFullscreen)
                appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
            else
                appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
        }

        private async Task <Process?> SafeStartProcessAsync(ProcessStartInfo info, string stepName)
        {
            try
            {
                if (!File.Exists(info.FileName))
                {
                    CMDebugInfo += $"\n[{stepName}] ERROR: Executable not found: {info.FileName}";
                    return null;
                }

                var p = Process.Start(info);
                if (p == null)
                {
                    CMDebugInfo += $"\n[{stepName}] ERROR: Failed to start process.";
                }
                return p;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                CMDebugInfo += $"\n[{stepName}] Win32Exception: {ex.Message} (Code: {ex.NativeErrorCode}). Path: {info.FileName}";
                System.Diagnostics.Debug.WriteLine($"[{stepName}] Win32Exception: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                CMDebugInfo += $"\n[{stepName}] Exception: {ex.Message}";
                return null;
            }
        }
    }
}

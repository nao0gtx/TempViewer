using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Threading.Tasks;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime; // Required for AsStream() extension
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT;
using WinRT.Interop;
using TransparentWinUI3.Views;
using TransparentWinUI3.Services;

namespace TransparentWinUI3
{
    public sealed partial class MainWindow : Window
    {
        private DesktopAcrylicController? m_acrylicController;
        private SystemBackdropConfiguration? m_configurationSource;
        private LibRawHelper? m_libRawHelper;
        private FFmpegHelper? m_ffmpegHelper;
        private byte _toolbarOpacityByte = SettingsService.Current.ToolbarOpacityByte;
        private bool _fastLoadEnabled = SettingsService.Current.FastLoadEnabled; 
        private int _screenDecodeWidth = SettingsService.Current.ScreenDecodeWidth;
        private ColorManagedWindow? _activeCmsWindow; // 当前打开的色彩管理预览窗口

        // RAW 格式扩展名集合
        private static readonly System.Collections.Generic.HashSet<string> RawExtensions = new(
            System.StringComparer.OrdinalIgnoreCase)
        {
            ".nef", ".cr2", ".cr3", ".arw", ".dng", ".raf",
            ".orf", ".rw2", ".pef", ".srw", ".x3f"
        };

        // 浏览时支持的普通图片扩展名（排除 RAW）
        private static readonly System.Collections.Generic.HashSet<string> BrowsingExtensions = new(
            System.StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif",
            ".webp", ".avif", ".heic", ".heif", ".jxl", ".svg", ".ico",
            ".exr", ".hdr", ".psd", ".jp2"
        };

        // WIC 原生支持的格式（无需 FFmpeg，直接解码，速度最快）
        private static readonly System.Collections.Generic.HashSet<string> NativeFormats = new(
            System.StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif",
            ".ico", ".webp",
            // 以下格式需要安装对应 Windows 编解码器扩展
            ".heic", ".heif", ".avif"
        };

        // Tab 数据模型
        private class ImageTabData
        {
            public string? FileName { get; set; }
            public string? FilePath { get; set; }
            public ImageSource? ImageSource { get; set; } // Changed from BitmapSource to ImageSource to support SoftwareBitmapSource
            public int OriginalWidth { get; set; }   // 文件实际像素宽度（未解码限制）
            public int OriginalHeight { get; set; }  // 文件实际像素高度
            public bool IsFullResLoaded { get; set; } = false; // 是否已加载原图
            public System.Collections.Generic.List<string>? FolderFiles { get; set; }
            public int CurrentIndex { get; set; }
        }

        public MainWindow()
        {
            this.InitializeComponent();

            // 初始化helpers
            m_libRawHelper = new LibRawHelper();
            m_ffmpegHelper = new FFmpegHelper();

            // 设置自定义标题栏
            SetupCustomTitleBar();

            // 设置 Acrylic 背景
            SetupAcrylicBackdrop();

            // 设置窗体大小
            this.AppWindow.Resize(new Windows.Graphics.SizeInt32(1200, 800));

            this.Closed += (s, e) =>
            {
                _activeCmsWindow?.Close();
            };

            // 获取主屏幕宽度，用于解码提示（自动适配 4K）
            try
            {
                var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                    this.AppWindow.Id,
                    Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                _screenDecodeWidth = displayArea.OuterBounds.Width;
                Debug.WriteLine($"[Init] Screen decode width: {_screenDecodeWidth}");
            }
            catch { _screenDecodeWidth = 1920; }

            // 居中窗口
            CenterWindow();

            // 监听大小变化以更新拖拽区域
            this.SizeChanged += MainWindow_SizeChanged;
            
            // 重要：初次启动激活时也刷新拖拽区域，防止启动即无法拖动
            this.Activated += (s, e) => {
                if (e.WindowActivationState != WindowActivationState.Deactivated)
                {
                    UpdateTitleBarDragRegions();
                }
            };

            // 手动触发一次
            UpdateTitleBarDragRegions();
        }

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
        {
            UpdateTitleBarDragRegions();
        }

        private double GetScaleAdjustment()
        {
            // 在 WinUI 3 中，XamlRoot.RasterizationScale 是获取 DPI 缩放最准确的方式
            if (this.Content != null && this.Content.XamlRoot != null)
            {
                return this.Content.XamlRoot.RasterizationScale;
            }
            return 1.0;
        }

        private void UpdateTitleBarDragRegions()
        {
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = this.AppWindow.TitleBar;
                double scale = GetScaleAdjustment();
                
                // 使用 AppWindow.Size.Width (原始像素) 确保覆盖全宽
                // 高度通常为 48 像素，需乘以缩放系数
                var rect = new Windows.Graphics.RectInt32
                {
                    X = 0,
                    Y = 0,
                    Width = this.AppWindow.Size.Width,
                    Height = (int)(48 * scale)
                };
                
                titleBar.SetDragRectangles(new[] { rect });
                Debug.WriteLine($"[Setup] Drag regions updated: {rect.Width}x{rect.Height} at scale {scale}");
            }
        }

        private void SetupCustomTitleBar()
        {
            Debug.WriteLine("[Setup] Setting up custom title bar...");
            
            // 扩展标题栏到内容区域
            this.ExtendsContentIntoTitleBar = true;
            
            // 设置标题栏拖拽区域（避免与按钮冲突）
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = this.AppWindow.TitleBar;
                titleBar.ExtendsContentIntoTitleBar = true;
                titleBar.ButtonBackgroundColor = Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                Debug.WriteLine("[Setup] ✓ Custom title bar enabled");
            }
            else
            {
                Debug.WriteLine("[Setup] ✗ Custom title bar not supported");
            }
        }

        private void SetupAcrylicBackdrop()
        {
            Debug.WriteLine("[Setup] Setting up custom Acrylic backdrop...");
            if (DesktopAcrylicController.IsSupported())
            {
                m_acrylicController = new DesktopAcrylicController();
                
                // 设置配置源
                m_configurationSource = new SystemBackdropConfiguration();
                this.Activated += (s, e) => { if (m_configurationSource != null) m_configurationSource.IsInputActive = e.WindowActivationState != WindowActivationState.Deactivated; };
                this.Closed += (s, e) => {
                    if (m_configurationSource != null) m_configurationSource = null;
                    if (m_acrylicController != null) {
                        m_acrylicController.Dispose();
                        m_acrylicController = null;
                    }
                };

                // 设置透明度属性（增加透明度）
                m_acrylicController.TintColor = Colors.White;
                float tint = (float)(SettingsService.Current.CustomSettings.TryGetValue("TintOpacity", out var t) && float.TryParse(t, out var tf) ? tf : 0.15f);
                m_acrylicController.TintOpacity = tint; 
                m_acrylicController.LuminosityOpacity = tint * 0.5f;

                // 应用背景
                m_acrylicController.AddSystemBackdropTarget(this.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
                m_acrylicController.SetSystemBackdropConfiguration(m_configurationSource);
                
                Debug.WriteLine("[Setup] ✓ Enhanced Acrylic enabled");
            }
            else
            {
                Debug.WriteLine("[Setup] ✗ Acrylic not supported");
            }
        }

        private void CenterWindow()
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow != null)
            {
                var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
                if (displayArea != null)
                {
                    var centerX = (displayArea.WorkArea.Width - appWindow.Size.Width) / 2;
                    var centerY = (displayArea.WorkArea.Height - appWindow.Size.Height) / 2;
                    appWindow.Move(new Windows.Graphics.PointInt32(centerX, centerY));
                }
            }
        }

        // Menu: Open Regular Image
        private async void OpenImageMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await OpenRegularImageFileAsync();
        }

        // Menu: Open RAW
        private async void OpenRawMenuItem_Click(object sender, RoutedEventArgs e)
        {
            await OpenRawFileAsync();
        }

        // Menu: Exit
        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // 拖放支持
        private void RootGrid_DragOver(object sender, DragEventArgs e)
        {
            // 检查是否包含文件
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "打开图片";
                e.DragUIOverride.IsCaptionVisible = true;
                e.DragUIOverride.IsGlyphVisible = true;
            }
            else
            {
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
            }
        }

        private async void RootGrid_Drop(object sender, DragEventArgs e)
        {
            if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
                return;

            var items = await e.DataView.GetStorageItemsAsync();
            foreach (var item in items)
            {
                if (item is StorageFile file)
                {
                    string ext = Path.GetExtension(file.Path).ToLowerInvariant();
                    // 过滤非图片文件
                    bool isSupported = NativeFormats.Contains(ext) || RawExtensions.Contains(ext) ||
                                       BrowsingExtensions.Contains(ext);
                    if (isSupported)
                    {
                        bool isRaw = RawExtensions.Contains(ext);
                        await LoadImageInNewTabAsync(file, isRawFile: isRaw);
                    }
                }
            }
        }

        // TabView: Add Tab Button Click — 打开统一文件选择器（包含所有格式）
        private async void TabView_AddTabButtonClick(TabView sender, object args)
        {
            await OpenAllFormatsFileAsync();
        }

        /// <summary>
        /// 打开所有格式的统一文件选择器，自动根据扩展名判断是否为 RAW
        /// </summary>
        private async Task OpenAllFormatsFileAsync()
        {
            var picker = new FileOpenPicker();
            var hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hwnd);

            // 普通图片格式
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".gif");
            picker.FileTypeFilter.Add(".tiff");
            picker.FileTypeFilter.Add(".tif");
            picker.FileTypeFilter.Add(".webp");
            picker.FileTypeFilter.Add(".avif");
            picker.FileTypeFilter.Add(".heic");
            picker.FileTypeFilter.Add(".heif");
            picker.FileTypeFilter.Add(".jxl");
            picker.FileTypeFilter.Add(".svg");
            picker.FileTypeFilter.Add(".ico");
            picker.FileTypeFilter.Add(".exr");
            picker.FileTypeFilter.Add(".hdr");
            picker.FileTypeFilter.Add(".psd");
            picker.FileTypeFilter.Add(".jp2");
            // RAW 格式
            picker.FileTypeFilter.Add(".nef");
            picker.FileTypeFilter.Add(".cr2");
            picker.FileTypeFilter.Add(".cr3");
            picker.FileTypeFilter.Add(".arw");
            picker.FileTypeFilter.Add(".dng");
            picker.FileTypeFilter.Add(".raf");
            picker.FileTypeFilter.Add(".orf");
            picker.FileTypeFilter.Add(".rw2");
            picker.FileTypeFilter.Add(".pef");
            picker.FileTypeFilter.Add(".srw");
            picker.FileTypeFilter.Add(".x3f");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                string ext = Path.GetExtension(file.Path);
                bool isRaw = RawExtensions.Contains(ext);
                await LoadImageInNewTabAsync(file, isRawFile: isRaw);
            }
        }

        // TabView: Tab Close Requested
        private void TabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            sender.TabItems.Remove(args.Tab);
            
            // 如果所有图片标签都关闭了，显示"开始"页和中央提示
            if (sender.TabItems.Count == 0)
            {
                sender.TabItems.Add(HomeTab);
                if (EmptyStateOverlay != null) EmptyStateOverlay.Visibility = Visibility.Visible;
            }
        }

        // TabView: Selection Changed
        private async void TabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_activeCmsWindow != null && ImageTabView.SelectedItem is TabViewItem tabItem && tabItem.Tag is Image image && image.Tag is ImageTabData data)
            {
                if (!string.IsNullOrEmpty(data.FilePath))
                {
                    await _activeCmsWindow.UpdateImageAsync(data.FilePath);
                }
            }
        }

        /// <summary>
        /// 打开普通图片文件（FFmpeg 支持的格式）
        /// </summary>
        private async Task OpenRegularImageFileAsync()
        {
            var picker = new FileOpenPicker();
            var hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hwnd);

            // 普通图片格式
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".gif");
            picker.FileTypeFilter.Add(".tiff");
            picker.FileTypeFilter.Add(".tif");
            picker.FileTypeFilter.Add(".webp");
            // FFmpeg 支持的扩展格式
            picker.FileTypeFilter.Add(".avif");
            picker.FileTypeFilter.Add(".heic");
            picker.FileTypeFilter.Add(".heif");
            picker.FileTypeFilter.Add(".jxl");   // JPEG XL
            picker.FileTypeFilter.Add(".svg");
            picker.FileTypeFilter.Add(".ico");
            picker.FileTypeFilter.Add(".exr");   // OpenEXR
            picker.FileTypeFilter.Add(".hdr");   // Radiance HDR
            picker.FileTypeFilter.Add(".psd");   // Photoshop
            picker.FileTypeFilter.Add(".jp2");   // JPEG 2000

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await LoadImageInNewTabAsync(file, isRawFile: false);
            }
        }

        /// <summary>
        /// 打开 RAW 格式文件（LibRaw 处理）
        /// </summary>
        private async Task OpenRawFileAsync()
        {
            var picker = new FileOpenPicker();
            var hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hwnd);

            // RAW 格式
            picker.FileTypeFilter.Add(".nef");   // Nikon
            picker.FileTypeFilter.Add(".cr2");   // Canon
            picker.FileTypeFilter.Add(".cr3");   // Canon (new)
            picker.FileTypeFilter.Add(".arw");   // Sony
            picker.FileTypeFilter.Add(".dng");   // Adobe DNG
            picker.FileTypeFilter.Add(".raf");   // Fujifilm
            picker.FileTypeFilter.Add(".orf");   // Olympus
            picker.FileTypeFilter.Add(".rw2");   // Panasonic
            picker.FileTypeFilter.Add(".pef");   // Pentax
            picker.FileTypeFilter.Add(".srw");   // Samsung
            picker.FileTypeFilter.Add(".x3f");   // Sigma

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await LoadImageInNewTabAsync(file, isRawFile: true);
            }
        }

        private async Task LoadImageInNewTabAsync(StorageFile file, bool isRawFile)
        {
            try
            {
                // 使用路径加载，避免 StorageFile BUG
                // 使用路径加载，避免 StorageFile BUG
                var (imageSource, width, height, isThumbnail) = await LoadImageSourceAsync(file.Path);

                if (imageSource != null)
                {
                    Debug.WriteLine($"[Tab] Image loaded successfully: {width}x{height}");
                    
                    if (ImageTabView.TabItems.Contains(HomeTab))
                    {
                        ImageTabView.TabItems.Remove(HomeTab);
                    }
                    if (EmptyStateOverlay != null) EmptyStateOverlay.Visibility = Visibility.Collapsed;
                    
                    await CreateImageTab(file.Name, file.Path, imageSource, width, height, isThumbnail);
                }
                else
                {
                    Debug.WriteLine($"[Tab] ✗ Failed to load image source");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Tab] Error loading image: {ex.Message}");
                ShowErrorDialog("加载失败", $"无法加载图片：{ex.Message}");
            }
        }

        private async Task<(ImageSource? source, int width, int height, bool isThumbnail)> LoadImageSourceAsync(string path, int? decodeWidthOverride = null)
        {
            Debug.WriteLine($"\n[Load] ========== Loading: {path} ==========");
            ImageSource? imageSource = null;
            int width = 0;
            int height = 0;
            string ext = Path.GetExtension(path);
            bool isRaw = RawExtensions.Contains(ext);
            bool isNative = NativeFormats.Contains(ext);

            // 确定解码宽度
            // 如果提供了override值，则使用override值 (0 表示原图)
            // 否则使用全局设置 (_fastLoadEnabled ? _screenDecodeWidth : 0)
            int targetDecodeWidth = decodeWidthOverride ?? (_fastLoadEnabled ? _screenDecodeWidth : 0);

            // 1. 尝试 WIC 原生加载（最快路径）
            // 对于 JPEG/PNG/BMP/GIF/TIFF/WebP/HEIC/AVIF 等，无需 FFmpeg
            if (!isRaw && isNative)
            {
                try
                {
                    // 在后台线程读取文件内容
                    byte[] fileBytes = await Task.Run(() => File.ReadAllBytes(path));
                    var ms = new System.IO.MemoryStream(fileBytes);
                    var raStream = ms.AsRandomAccessStream();

                    var decoder = await BitmapDecoder.CreateAsync(raStream);
                    
                    // 使用 BitmapTransform 进行缩放 (Fast Load)
                    // 使用 BitmapTransform 进行缩放 (Fast Load)
                    // 修复：计算缩放比例时必须考虑 Orientation
                    var transform = new BitmapTransform();
                    
                    // 获取原始宽和高（未旋转）
                    uint originalWidth = decoder.PixelWidth;
                    uint originalHeight = decoder.PixelHeight;

                    // 检查是否需要交换宽高（90或270度旋转）
                    bool isRotated = false;
                    try
                    {
                        // 试图获取 Orientation 属性
                        // System.Photo.Orientation = 274
                        // 或者直接用 decoder.OrientedPixelWidth 判断是否不同
                        if (decoder.OrientedPixelWidth != decoder.PixelWidth)
                        {
                            isRotated = true;
                        }
                    }
                    catch {}
                    
                    // 逻辑宽和高（旋转后）
                    uint logicalWidth = isRotated ? originalHeight : originalWidth;
                    uint logicalHeight = isRotated ? originalWidth : originalHeight;

                    if (targetDecodeWidth > 0 && targetDecodeWidth < logicalWidth)
                    {
                        // 计算缩放比例
                        double scale = (double)targetDecodeWidth / logicalWidth;
                        
                        // 应用缩放
                        // 注意：BitmapTransform.ScaledWidth/Height 是作用于原始数据的
                        // 如果有旋转，这里怎么设？
                        // MS Docs: "ScaledWidth/Height" applies to the *result*.
                        // NO, Actually BitmapTransform applies AFTER decoding but BEFORE format conversion?
                        // Wait, "The scaling operation is verifying the result."
                        // Let's look at usage.
                        
                        // 关键点：当我们使用 GetPixelDataAsync 并且指定 RespectExifOrientation 时，
                        // 我们得到的像素数据已经是旋转过的。
                        // 但是 BitmapTransform 的 ScaledWidth/Height 是指 *输出结果* 的宽高吗？
                        // 测试结果显示：BitmapTransform 的 ScaledWidth 指定的是最终想要的宽度。
                        
                        transform.ScaledWidth = (uint)targetDecodeWidth;
                        transform.ScaledHeight = (uint)(logicalHeight * scale);
                        transform.InterpolationMode = BitmapInterpolationMode.Fant;
                        
                        Debug.WriteLine($"[Load] SoftwareBitmap: Downscaling to {transform.ScaledWidth}x{transform.ScaledHeight} (Oriented)");
                        Debug.WriteLine($"[Load] SoftwareBitmap: Downscaling to {transform.ScaledWidth}x{transform.ScaledHeight}");
                    }
                    else
                    {
                        Debug.WriteLine($"[Load] SoftwareBitmap: Full resolution load ({decoder.PixelWidth}x{decoder.PixelHeight})");
                    }
                    
                    // 统一使用 GetPixelDataAsync + WriteableBitmap
                    // 这是最底层的内存拷贝方式，不依赖 SoftwareBitmapSource 或 CopyToBuffer 的互操作层
                    // 彻底避开 Component Not Found (0x88982F50)
                    var pixelProvider = await decoder.GetPixelDataAsync(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied,
                        transform,
                        ExifOrientationMode.RespectExifOrientation,
                        ColorManagementMode.DoNotColorManage);

                    var pixelBytes = pixelProvider.DetachPixelData();
                    
                    // 计算实际宽高（应用 Transform 后的）
                    // 计算实际宽高（应用 Transform 后的）
                    // 修复：必须使用 OrientedPixelWidth/Height，因为我们请求了 RespectExifOrientation
                    // 否则对于带有旋转标签的竖版图片（物理存储为横版），宽高会搞反，导致显示错乱
                    int actualWidth = (int)decoder.OrientedPixelWidth;
                    int actualHeight = (int)decoder.OrientedPixelHeight;
                    if (transform.ScaledWidth > 0) 
                    {
                        actualWidth = (int)transform.ScaledWidth;
                        actualHeight = (int)transform.ScaledHeight;
                    }
                    
                    try 
                    {
                        var writeableBitmap = new WriteableBitmap(actualWidth, actualHeight);
                        using (var stream = writeableBitmap.PixelBuffer.AsStream())
                        {
                            await stream.WriteAsync(pixelBytes, 0, pixelBytes.Length);
                        }
                        imageSource = writeableBitmap;
                        Debug.WriteLine($"[Load] WriteableBitmap constructed via byte[] copy ({actualWidth}x{actualHeight})");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Load] WriteableBitmap construction failed: {ex.Message}");
                        throw; // Let native fallback catch or bubble up
                    }
                    
                    // 返回原始尺寸（用于 Zoom 逻辑）
                    // 返回原始尺寸（用于 Zoom 逻辑）
                    // 修复：使用 OrientedPixelWidth/Height
                    width = (int)decoder.OrientedPixelWidth;
                    height = (int)decoder.OrientedPixelHeight;
                    
                    Debug.WriteLine($"[Load] Native WIC (WriteableBitmap) load OK. Original: {width}x{height}");
                    return (imageSource, width, height, false);
                    
                    // 返回原始尺寸（用于 Zoom 逻辑），而不是缩放后的尺寸
                    width = (int)decoder.PixelWidth;
                    height = (int)decoder.PixelHeight;
                    
                    Debug.WriteLine($"[Load] Native WIC (SoftwareBitmap) load OK. Original: {width}x{height}");
                    return (imageSource, width, height, false); 
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Load] Native load failed, trying FFmpeg: {ex.Message}");
                }
            }

            // 2. 尝试 RAW 加载
            if (isRaw && m_libRawHelper != null && m_libRawHelper.IsLibRawAvailable())
            {
                try 
                {
                    // 如果没有强制请求原图 (decodeWidthOverride != 0)，尝试加载嵌入的预览图
                    if (decodeWidthOverride != 0)
                    {
                         var thumb = await m_libRawHelper.GetThumbnailAsync(path);
                         if (thumb != null)
                         {
                             Debug.WriteLine("[Load] RAW Thumbnail loaded");
                             return (thumb, thumb.PixelWidth, thumb.PixelHeight, true);
                         }
                    }

                    // 加载原图
                    imageSource = await m_libRawHelper.ProcessRawFromMemoryAsync(path);
                    if (imageSource != null)
                    {
                        if (imageSource is BitmapSource bs)
                        {
                            width = bs.PixelWidth;
                            height = bs.PixelHeight;
                        }
                        Debug.WriteLine("[Load] RAW load OK");
                        return (imageSource, width, height, false);
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[Load] RAW load failed: {ex.Message}"); }
            }

            // 3. FFmpeg 转换
            if (m_ffmpegHelper != null)
            {
                try
                {
                    string tempDir = Path.GetTempPath();
                    string tempPng = Path.Combine(tempDir, $"{Guid.NewGuid()}.png");
                    var result = await m_ffmpegHelper.ConvertImageToPngAsync(path, tempPng);
                    
                    if (result != null && File.Exists(tempPng))
                    {
                        byte[] pngBytes = await Task.Run(() => File.ReadAllBytes(tempPng));
                        try { File.Delete(tempPng); } catch { }
                        
                        var ms = new System.IO.MemoryStream(pngBytes);
                        var raStream = ms.AsRandomAccessStream();
                        var bitmapImage = new BitmapImage();
                        
                        // FFmpeg 转出的 PNG 也可以应用 DecodePixelWidth
                        if (targetDecodeWidth > 0)
                        {
                            bitmapImage.DecodePixelWidth = targetDecodeWidth;
                            bitmapImage.DecodePixelType = DecodePixelType.Logical;
                        }

                        await bitmapImage.SetSourceAsync(raStream);
                        
                        imageSource = bitmapImage;
                        width = bitmapImage.PixelWidth;
                        height = bitmapImage.PixelHeight;
                        Debug.WriteLine("[Load] FFmpeg load OK");
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[Load] FFmpeg load failed: {ex.Message}"); }
            }

            return (imageSource, width, height, false);
        }

        private async Task CreateImageTab(string fileName, string filePath, ImageSource imageSource, int width, int height, bool isThumbnail)
        {
            // 扫描文件夹获取同级图片（用于滚轮切换）
            // 策略：所有支持的格式都允许浏览，包括 RAW
            var folderFiles = new System.Collections.Generic.List<string>();
            int currentIndex = -1;
            
            // 修复 CS0103: 需要在此处定义 isRaw
            string ext = Path.GetExtension(filePath);
            bool isRaw = RawExtensions.Contains(ext);
            
            // 在后台线程扫描文件夹，避免阻塞 UI
            try
            {
                (folderFiles, currentIndex) = await Task.Run(() =>
                {
                    string? dir = Path.GetDirectoryName(filePath);
                    if (dir == null || !Directory.Exists(dir))
                        return (new System.Collections.Generic.List<string>(), -1);

                    var sortedFiles = new System.Collections.Generic.List<string>();
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        string ext = Path.GetExtension(f);
                        if (BrowsingExtensions.Contains(ext) || RawExtensions.Contains(ext))
                            sortedFiles.Add(f);
                    }
                    sortedFiles.Sort();
                    int idx = sortedFiles.IndexOf(filePath);
                    return (sortedFiles, idx);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FolderScan] Error: {ex.Message}");
            }

            // 创建显示容器
            var grid = new Grid
            {
                Background = new SolidColorBrush(Colors.Transparent),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var image = new Image
            {
                Source = imageSource,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = new ImageTabData
                {
                    FileName = fileName,
                    FilePath = filePath,
                    ImageSource = imageSource,
                    OriginalWidth = width,
                    OriginalHeight = height,
                    IsFullResLoaded = !isThumbnail,
                    FolderFiles = folderFiles,
                    CurrentIndex = currentIndex
                }
            };
            
            // 允许 Image 交互
            image.IsHitTestVisible = true;

            var viewbox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = image
            };

            grid.Children.Add(viewbox);

            var scrollViewer = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollMode = ScrollMode.Enabled, // 允许滚动以支持代码控制
                VerticalScrollMode = ScrollMode.Enabled,
                VerticalAlignment = VerticalAlignment.Stretch, 
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ZoomMode = ZoomMode.Disabled // 我们自己控制缩放
            };

            // 加载指示器（用于原图加载）
            var loadingRing = new ProgressRing { IsActive = true, Width = 32, Height = 32, Foreground = new SolidColorBrush(Colors.White) };
            var loadingText = new TextBlock { Text = "正在加载原图..." , Foreground = new SolidColorBrush(Colors.White), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center };
            var loadingPanel = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { loadingRing, loadingText } };
            
            var loadingOverlay = new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x80, 0, 0, 0)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                Child = loadingPanel,
                Tag = "LoadingOverlay" // 标记以便查找
            };
            
            grid.Children.Add(loadingOverlay);

            // 长按放大逻辑 & 拖动逻辑
            image.PointerPressed += (s, e) =>
            {
                var img = s as Image;
                if (img != null && e.GetCurrentPoint(img).Properties.IsLeftButtonPressed)
                {
                    StartPressAndHoldZoom(img, e, scrollViewer, viewbox, grid);
                }
            };

            // 拖动支持 (仅在 1:1 模式下生效)
            image.PointerMoved += (s, e) =>
            {
                if (_zoomTimer != null && !_zoomTimer.IsEnabled && _currentScrollViewer != null && 
                    _currentScrollViewer.Content == s) // 确认处于 1:1 模式
                {
                    var ptr = e.GetCurrentPoint(_currentScrollViewer);
                    if (ptr.Properties.IsLeftButtonPressed)
                    {
                        // 计算拖动 delta
                        // 需要记录上一次位置，这里简单实现：
                        // 由于 PointerMoved 频繁触发，我们需要类级别变量记录
                        HandleImageDrag(s as Image, e);
                    }
                }
            };

            // Tab 内容容器
            var tabContent = new Grid();
            tabContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            tabContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var toolbar = CreateToolbar();
            Grid.SetRow(toolbar, 0);

            // 左侧操作栏 (用于放置"渲染完整 RAW"等按钮)
            var leftActionPanel = new StackPanel 
            { 
                Orientation = Orientation.Horizontal, 
                HorizontalAlignment = HorizontalAlignment.Left, 
                Spacing = 8,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "LeftActionPanel" // 标记以便查找
            };
            toolbar.Children.Add(leftActionPanel);

            // 添加操作按钮 (保持固定顺序：CMS 预览 -> 渲染 RAW)
            
            // 1. CMS 预览按钮
            var cmsBtn = new Button
            {
                Content = "CMS 预览",
                Height = 32,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 0x00, 0x00, 0x00)),
                BorderThickness = new Thickness(0),
                Tag = "CMSButton"
            };
            ToolTipService.SetToolTip(cmsBtn, "打开独立的色彩管理预览窗口");
            cmsBtn.Click += (s, e) =>
            {
                if (_activeCmsWindow != null)
                {
                    _activeCmsWindow.Activate();
                    _ = _activeCmsWindow.UpdateImageAsync(filePath);
                }
                else
                {
                    try
                    {
                        var cmsWindow = new Views.ColorManagedWindow(filePath);
                        _activeCmsWindow = cmsWindow;
                        cmsWindow.Closed += (s2, e2) => { _activeCmsWindow = null; };
                        cmsWindow.Activate();
                    }
                    catch (Exception ex)
                    {
                        ShowErrorDialog("Error", $"Failed to launch CMS Window: {ex.Message}");
                    }
                }
            };
            leftActionPanel.Children.Add(cmsBtn);

            // 2. 渲染完整 RAW 按钮
            AddLoadRawButton(leftActionPanel, image, filePath, isThumbnail, isRaw);

            tabContent.Children.Add(toolbar);

            Grid.SetRow(grid, 1);
            tabContent.Children.Add(grid);

            var tabItem = new TabViewItem
            {
                Header = fileName,
                Content = tabContent,
                IconSource = new SymbolIconSource { Symbol = Symbol.Pictures },
                Tag = image // 方便索引
            };

            // 滚轮切换图片 (需在 tabItem 创建后绑定，以便捕获变量)
            tabContent.PointerWheelChanged += async (s, e) =>
            {
                // 排除 Ctrl (缩放)
                var properties = e.GetCurrentPoint(null).Properties;
                var keyState = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
                bool isCtrlDown = (keyState & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;

                if (!isCtrlDown)
                {
                    int delta = properties.MouseWheelDelta;
                    if (delta > 0) await NavigateToImage(tabItem, -1); // 上一张
                    else if (delta < 0) await NavigateToImage(tabItem, 1);  // 下一张
                    e.Handled = true;
                }
            };
            
            ImageTabView.TabItems.Add(tabItem);
            ImageTabView.SelectedItem = tabItem;
            UpdateEmptyState();
        }

        private Windows.Foundation.Point _lastDragPos;
        private bool _isDragging;

        private void HandleImageDrag(Image? image, PointerRoutedEventArgs e)
        {
            if (image == null || _currentScrollViewer == null) return;

            var currentPos = e.GetCurrentPoint(_currentScrollViewer).Position;
            
            if (!_isDragging) // 刚开始拖动
            {
                _isDragging = true;
                _lastDragPos = currentPos;
                return; 
            }

            double deltaX = currentPos.X - _lastDragPos.X;
            double deltaY = currentPos.Y - _lastDragPos.Y;

            // ScrollViewer 滚动方向是相反的：鼠标向右拖(deltaX > 0)，内容向右动，ScrollOffset 应该减小
            _currentScrollViewer.ChangeView(
                _currentScrollViewer.HorizontalOffset - deltaX,
                _currentScrollViewer.VerticalOffset - deltaY,
                null, true);

            _lastDragPos = currentPos;
        }

        // 导航图片
        private async Task NavigateToImage(object tabItemObj, int direction)
        {
            if (tabItemObj is not TabViewItem tabItem || 
                tabItem.Tag is not Image image || 
                image.Tag is not ImageTabData data ||
                data.FolderFiles == null || 
                data.FolderFiles.Count <= 1)
            {
                return;
            }

            int newIndex = data.CurrentIndex + direction;
            if (newIndex < 0) newIndex = data.FolderFiles.Count - 1; // 循环
            if (newIndex >= data.FolderFiles.Count) newIndex = 0;

            string nextFile = data.FolderFiles[newIndex];
            
            // 停止定时器，防止它在导航过程中触发 SwitchTo1to1Zoom
            _zoomTimer?.Stop();
            
            // 1. 优先清理旧资源（必须先断开大图连接，防止 Visual Tree 变动时触发显卡驱动异常）
            image.Source = null;
            GC.Collect();

            // 2. 如果当前处于放大模式，还原视图（此时 Image 已空，操作安全）
            if (_currentZoomImage == image)
            {
                RestoreToFitZoom();
            }

            try
            {
                // 使用 Path 字符串直接加载，避免 StorageFile COM 异常
                var (source, w, h, isThumb) = await LoadImageSourceAsync(nextFile);
                if (source != null)
                {
                    // 更新 Image
                    image.Source = source;
                    data.FileName = Path.GetFileName(nextFile);
                    data.FilePath = nextFile;
                    data.ImageSource = source;
                    data.OriginalWidth = w;
                    data.OriginalHeight = h;
                    data.CurrentIndex = newIndex;
                    data.IsFullResLoaded = !isThumb;
                    
                    // 如果切换到了新图片，且是缩略图，需要更新工具栏按钮
                    // 这里为了简单，如果用户切换图片，我们暂时不重建整个 Tab，
                    // 而是如果之前有"渲染RAW"按钮，可能需要移除或更新。
                    // 但目前的架构是 Image 存在于 Grid 中， Toolbar 独立。
                    // 完美的做法是重建 Tab 或动态更新 Toolbar。
                    // 考虑到复杂度，这里简易处理：切换图片时，如果变成了缩略图，我们暂时无法在当前 Tab 动态添加按钮
                    // (因为没有保留 toolbar 引用)。
                    // 修正：我们应该在 NavigateToImage 中重建 Tab 吗？ 不，太慢。
                    // 妥协方案：在 NavigateToImage 中暂不支持动态添加"加载RAW"按钮，或者简单点，
                    // 让用户如果是浏览模式，默认行为即可。这个"加载RAW"按钮主要针对单张打开的 RAW 场景。
                    // 如果用户想要看某张图的 RAW，可以右键打开新 Tab (暂不支持)，或者我们只针对初始打开的 Tab 提供按钮。
                    
                    // 实际上，如果 data.IsFullResLoaded 为 false，Zoom 1:1 时会自动加载原图，这也算一种"加载"方式。
                    // 所以浏览模式下依靠 Zoom 加载原图是可接受的。
                    
                    // 更新 Tab Header
                    tabItem.Header = data.FileName;

                    // 同步 CMS 窗口
                    if (_activeCmsWindow != null && !string.IsNullOrEmpty(nextFile))
                    {
                        _ = _activeCmsWindow.UpdateImageAsync(nextFile);
                    }

                    // === 更新工具栏逻辑 ===
                    if (tabItem.Content is Grid tabContent && tabContent.Children.Count > 0 && tabContent.Children[0] is Grid toolbar)
                    {
                        // 查找 LeftActionPanel
                        var leftPanel = toolbar.Children.OfType<StackPanel>().FirstOrDefault(p => (p.Tag as string) == "LeftActionPanel");
                        if (leftPanel != null)
                        {
                            // 查找现有的 LoadRawButton
                            var existingBtn = leftPanel.Children.OfType<Button>().FirstOrDefault(b => (b.Tag as string) == "LoadRawButton");
                            
                            // 检查新图片是否是 RAW
                            // 检查新图片是否是 RAW
                            string newExt = Path.GetExtension(nextFile);
                            bool newIsRaw = RawExtensions.Contains(newExt);
                            // 修复 CS0136: isThumb 已在 line 885 定义，直接使用或使用新名称
                            // 这里我们实际想表达的是 data 里的状态，但 line 896 已经更新了 data
                            // data.IsFullResLoaded = !isThumb (来自 LoadImageSourceAsync)
                            // 所以 !data.IsFullResLoaded 等于 isThumb
                            bool isCurrentThumb = !data.IsFullResLoaded;

                            if (existingBtn != null)
                            {
                                // 更新按钮状态
                                UpdateLoadRawButtonState(existingBtn, newIsRaw, isCurrentThumb);
                                
                                // 更新点击事件绑定 (因为 filePath 变了)
                                // 最简单的方式是移除旧的，添加新的 (带着正确的状态)
                                leftPanel.Children.Remove(existingBtn);
                                AddLoadRawButton(leftPanel, image, nextFile, isCurrentThumb, newIsRaw);
                            }
                            else
                            {
                                // 如果没按钮 (理论上应该一直有)，添加一个
                                AddLoadRawButton(leftPanel, image, nextFile, isCurrentThumb, newIsRaw);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Nav] Error: {ex.Message}");
            }
        }

        private DispatcherTimer? _zoomTimer;
        private Image? _currentZoomImage;
        private ScrollViewer? _currentScrollViewer;
        private Viewbox? _currentViewbox;
        private Grid? _currentGrid;

        private void StartPressAndHoldZoom(Image image, PointerRoutedEventArgs e, ScrollViewer scrollViewer, Viewbox viewbox, Grid grid)
        {
            // 取消之前的定时器
            _zoomTimer?.Stop();

            _currentZoomImage = image;
            _currentScrollViewer = scrollViewer;
            _currentViewbox = viewbox;
            _currentGrid = grid;

            // 创建定时器（300ms后触发1:1放大）
            _zoomTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };

            _zoomTimer.Tick += async (s, args) =>
            {
                _zoomTimer?.Stop();
                // 1. 初始化拖动状态
                _isDragging = false; 
                await SwitchTo1to1Zoom(image, e, scrollViewer, viewbox, grid);
            };

            _zoomTimer.Start();

            // 监听PointerReleased
            image.PointerReleased += OnImagePointerReleased;
            image.PointerCanceled += OnImagePointerReleased;
        }

        private void OnImagePointerReleased(object sender, PointerRoutedEventArgs e)
        {
            var image = sender as Image;
            if (image != null)
            {
                // 取消定时器
                _zoomTimer?.Stop();

                // 如果已经是1:1模式，恢复到缩放模式
                if (_currentViewbox != null && _currentViewbox.Parent == null)
                {
                    RestoreToFitZoom();
                }

                // 取消订阅
                image.PointerReleased -= OnImagePointerReleased;
                image.PointerCanceled -= OnImagePointerReleased;
            }
        }

        private async Task SwitchTo1to1Zoom(Image image, PointerRoutedEventArgs e, ScrollViewer scrollViewer, Viewbox viewbox, Grid grid)
        {
            var data = image.Tag as ImageTabData;
            if (data == null) return;

            // 检查是否需要加载原图
            // 修改：只有在用户明确点击"渲染完整RAW"时才加载原图
            // 这里的自动加载逻辑移除
            /*
            // 条件：开启了快速加载 AND 原图未加载
            // 由于都使用了 SoftwareBitmapSource，我们不再检查 PixelWidth，直接依赖 IsFullResLoaded 标志
            bool needLoadFull = _fastLoadEnabled && !data.IsFullResLoaded;

            if (needLoadFull && data.FilePath != null)
            {
                 // ... removed auto load logic ...
            }
            */

            // 获取当前（缩放后）的点击位置和图片实际显示尺寸

            // 获取当前（缩放后）的点击位置和图片实际显示尺寸
            var pos = e.GetCurrentPoint(image).Position;
            double actualWidth = image.ActualWidth;
            double actualHeight = image.ActualHeight;

            // 计算点击位置在原图上的比例
            double ratioX = pos.X / actualWidth;
            double ratioY = pos.Y / actualHeight;

            // 计算原图上的坐标
            double origX = ratioX * data.OriginalWidth;
            double origY = ratioY * data.OriginalHeight;

            Debug.WriteLine($"[Zoom] Click: {pos.X},{pos.Y} on {actualWidth}x{actualHeight} -> Orig: {origX},{origY}");

            // 切换模式：从viewbox移到scrollviewer，并设置为不拉伸
            // 防止重复插入（如长按触发两次）
            if (grid.Children.Contains(scrollViewer))
            {
                Debug.WriteLine("[Zoom] SwitchTo1to1Zoom skipped - ScrollViewer already in grid");
                return;
            }

            try
            {
                viewbox.Child = null;
                grid.Children.Remove(viewbox);

                image.Stretch = Stretch.None;
                scrollViewer.Content = image;
                grid.Children.Insert(0, scrollViewer);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Zoom] SwitchTo1to1Zoom tree op failed: {ex.Message}");
                // 尝试回滚
                try { viewbox.Child = image; } catch { }
                try { if (!grid.Children.Contains(viewbox)) grid.Children.Insert(0, viewbox); } catch { }
                return;
            }

            // 强制布局更新以确保scrollviewer知道新尺寸
            image.UpdateLayout();
            scrollViewer.UpdateLayout();

            // 重新获取 Image 在 ScrollViewer 中的实际尺寸（可能受 DPI 缩放影响）
            double newImageWidth = image.ActualWidth;
            double newImageHeight = image.ActualHeight;
            
            // 获取 ScrollViewer 的可视区域大小
            double viewportWidth = scrollViewer.ViewportWidth;
            double viewportHeight = scrollViewer.ViewportHeight;

            // 如果 Viewport 尚未更新（通常 UpdateLayout 后应该更新），尝试使用 ActualWidth
            if (viewportWidth == 0) viewportWidth = scrollViewer.ActualWidth;
            if (viewportHeight == 0) viewportHeight = scrollViewer.ActualHeight;

            // 计算新的中心点坐标
            double targetX = ratioX * newImageWidth;
            double targetY = ratioY * newImageHeight;

            // 滚动到中心位置
            // 注意：ChangeView 的坐标是相对于 ScrollViewer 内容的偏移
            scrollViewer.ChangeView(targetX - viewportWidth / 2,
                                   targetY - viewportHeight / 2,
                                   null,
                                   true);

            Debug.WriteLine($"[Zoom] Switched to 1:1. Target: {targetX},{targetY} Viewport: {viewportWidth}x{viewportHeight}");
        }

        private void RestoreToFitZoom()
        {
            if (_currentScrollViewer == null || _currentViewbox == null || _currentGrid == null || _currentZoomImage == null)
                return;

            // 防止重复还原：如果 Viewbox 已经在 Grid 中，说明已经还原过
            if (_currentGrid.Children.Contains(_currentViewbox))
            {
                Debug.WriteLine("[Zoom] RestoreToFitZoom skipped - already in fit mode");
                _currentScrollViewer = null;
                _currentZoomImage = null;
                _currentViewbox = null;
                _currentGrid = null;
                return;
            }

            try
            {
                // 恢复模式：设置为均匀拉伸，放回 viewbox
                _currentZoomImage.Stretch = Stretch.Uniform;
                _currentScrollViewer.Content = null;
                _currentGrid.Children.Remove(_currentScrollViewer);

                _currentViewbox.Child = _currentZoomImage;
                _currentGrid.Children.Insert(0, _currentViewbox);

                Debug.WriteLine("[Zoom] Restored to fit");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Zoom] RestoreToFitZoom failed (ignoring): {ex.Message}");
            }
            finally
            {
                // 无论成功与否，重置状态字段
                _currentScrollViewer = null;
                _currentZoomImage = null;
                _currentViewbox = null;
                _currentGrid = null;
            }
        }

        private void UpdateEmptyState()
        {
            // 已废弃，通过 HomeTab 动态管理
        }

        private async void ShowErrorDialog(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "确定",
                XamlRoot = this.Content.XamlRoot
            };
            await dialog.ShowAsync();
        }

        /// <summary>
        /// 创建工具栏 Grid（每个 Tab 共享相同样式）
        /// </summary>
        private Grid CreateToolbar()
        {
            var toolbar = new Grid
            {
                Height = 40,
                Padding = new Thickness(12, 0, 12, 0),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(_toolbarOpacityByte, 0xFF, 0xFF, 0xFF))
            };

            // 底部分隔线
            var border = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Bottom
            };
            toolbar.Children.Add(border);

            // 添加 GPU 测试按钮
            var gpuBtn = new Button
            {
                Content = new FontIcon { Glyph = "\uE74C", FontSize = 14 }, // OEM Icon
                Width = 32, Height = 32,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            ToolTipService.SetToolTip(gpuBtn, "测试 GPU 加速 (Experimental)");
            
            // 绑定事件 (需要通过 Tag 传递上下文，比较麻烦，这里简化为查找 Parent)
            // 先不绑定 Tag，而是通过 click event 的 sender 向上查找
            // 或者：CreateToolbar 是在 CreateImageTab 调用的，我们可以把 image 传进去
            // 修改 CreateToolbar 签名比较好
            
            // 临时方案：按钮 Tag 存储 Image 对象（需修改 CreateToolbar 调用处）
            
            // 修正：CreateToolbar 无法访问 Image，尚未创建。
            // 解决方案：在 CreateImageTab 中创建完 Toolbar 后，手动把按钮加进去
            // 或者：让 CreateToolbar 返回 (Grid, Button) 元组
            
            // 让我们保持简单：CreateToolbar 只负责通用样式，
            // 按钮在 CreateImageTab 里面加
            
            return toolbar;
        }

        private async void OnTestGpuDebayerClick(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            if (btn == null) return;

            btn.IsEnabled = false; // Disable button immediately

            try 
            {
                if (btn.Tag is Image image && image.Tag is ImageTabData data)
                {
                    // 1. Check File Type
                    string ext = Path.GetExtension(data.FilePath).ToLowerInvariant();
                    // Simple check, real app has list
                    if (string.IsNullOrEmpty(ext))
                    {
                        ShowErrorDialog("Error", "File extension not found.");
                        return;
                    }

                    // 2. Get Raw Data (CPU)
                    // Note: This now returns a log string too
                    var (rawData, w, h, pattern, log) = await m_libRawHelper.GetRawBayerDataAsync(data.FilePath);

                    if (rawData == null)
                    {
                        ShowErrorDialog("Error", $"Failed to read RAW data.\n{log}");
                        return;
                    }

                    // 3. Show Debug Dialog (Programmatic to avoid XAML crashes)
                    var dialog = new ContentDialog
                    {
                        XamlRoot = this.Content.XamlRoot,
                        Title = "GPU Debug",
                        PrimaryButtonText = "Render",
                        CloseButtonText = "Cancel",
                        DefaultButton = ContentDialogButton.Primary
                    };

                    var stackPanel = new StackPanel { Spacing = 10, MinWidth = 400 };
                    
                    // Width
                    var widthInput = new TextBox { Header = "Width", Text = w.ToString(), InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } } };
                    stackPanel.Children.Add(widthInput);

                    // Height
                    var heightInput = new TextBox { Header = "Height", Text = h.ToString(), InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } } };
                    stackPanel.Children.Add(heightInput);

                    // Pattern
                    var patternCombo = new ComboBox { Header = "Pattern", HorizontalAlignment = HorizontalAlignment.Stretch };
                    patternCombo.Items.Add("RGGB (0)");
                    patternCombo.Items.Add("BGGR (1)");
                    patternCombo.Items.Add("GRBG (2)");
                    patternCombo.Items.Add("GBRG (3)");
                    
                    // Map Enum (FourCC) to Index (0-3)
                    int pIndex = 0;
                    if (pattern == LibRawNative.BayerPattern.BGGR) pIndex = 1;
                    else if (pattern == LibRawNative.BayerPattern.GRBG) pIndex = 2;
                    else if (pattern == LibRawNative.BayerPattern.GBRG) pIndex = 3;
                    patternCombo.SelectedIndex = pIndex;
                    stackPanel.Children.Add(patternCombo);

                    // Exposure
                    var exposureInput = new TextBox { Header = "Exposure (Default 1.0)", Text = "1.0", InputScope = new InputScope { Names = { new InputScopeName(InputScopeNameValue.Number) } } };
                    stackPanel.Children.Add(exposureInput);

                    // Log
                    var logBox = new TextBox 
                    { 
                        Header = "Debug Log", 
                        Text = log, 
                        IsReadOnly = true, 
                        Height = 150, 
                        AcceptsReturn = true, 
                        FontFamily = new FontFamily("Consolas")
                    };
                    ScrollViewer.SetVerticalScrollBarVisibility(logBox, ScrollBarVisibility.Auto);
                    stackPanel.Children.Add(logBox);

                    dialog.Content = stackPanel;

                    var result = await dialog.ShowAsync();

                    if (result == ContentDialogResult.Primary)
                    {
                        // 4. Run GPU Process with overridden params
                        int.TryParse(widthInput.Text, out int newW);
                        int.TryParse(heightInput.Text, out int newH);
                        float.TryParse(exposureInput.Text, out float newExposure);
                        if (newExposure <= 0) newExposure = 1.0f;
                        
                        LibRawNative.BayerPattern newPattern = LibRawNative.BayerPattern.RGGB; 
                        switch (patternCombo.SelectedIndex)
                        {
                            case 0: newPattern = LibRawNative.BayerPattern.RGGB; break;
                            case 1: newPattern = LibRawNative.BayerPattern.BGGR; break;
                            case 2: newPattern = LibRawNative.BayerPattern.GRBG; break;
                            case 3: newPattern = LibRawNative.BayerPattern.GBRG; break;
                        }

                        var sw = Stopwatch.StartNew();
                        var gpuProcessor = new GpuImageProcessor();
                        var processedBitmap = await gpuProcessor.RenderBayerToBitmapAsync(rawData, newW, newH, newPattern, newExposure);
                        sw.Stop();

                        if (processedBitmap != null)
                        {
                            image.Source = processedBitmap;
                            ShowErrorDialog("GPU Render Success", $"Time: {sw.ElapsedMilliseconds}ms\nSize: {newW}x{newH}");
                        }
                        else
                        {
                            ShowErrorDialog("GPU Render Failed", "Starting GPU processing failed. Check debug output.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ShowErrorDialog("Exception", ex.Message);
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }

        /// <summary>
        /// 设置菜单点击 - 打开透明度设置对话框
        /// </summary>
        private async void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // 当前值
            double currentWindowTint = m_acrylicController?.TintOpacity ?? 0.15;
            double currentToolbarPct = _toolbarOpacityByte / 255.0 * 100.0;

            var settingsPanel = new StackPanel { Spacing = 24, MinWidth = 340 };

            // ── 窗体透明度 ──
            var windowLabel = new TextBlock
            {
                Text = $"窗体透明度: {(int)(currentWindowTint * 100)}%",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            var windowSlider = new Slider
            {
                Minimum = 0, Maximum = 100,
                Value = currentWindowTint * 100,
                StepFrequency = 1
            };
            windowSlider.ValueChanged += (s, args) =>
            {
                double val = args.NewValue / 100.0;
                if (m_acrylicController != null)
                {
                    m_acrylicController.TintOpacity = (float)val;
                    m_acrylicController.LuminosityOpacity = (float)(val * 0.5);
                }
                windowLabel.Text = $"窗体透明度: {(int)args.NewValue}%";
                SettingsService.Current.CustomSettings["TintOpacity"] = val.ToString();
                SettingsService.Save();
            };
            settingsPanel.Children.Add(windowLabel);
            settingsPanel.Children.Add(windowSlider);

            // ── 标签栏/工具栏透明度 ──
            var tabLabel = new TextBlock
            {
                Text = $"标签栏/工具栏透明度: {(int)currentToolbarPct}%",
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            var tabSlider = new Slider
            {
                Minimum = 0, Maximum = 100,
                Value = currentToolbarPct,
                StepFrequency = 1
            };
            tabSlider.ValueChanged += (s, args) =>
            {
                byte alpha = (byte)(args.NewValue / 100.0 * 255);
                _toolbarOpacityByte = alpha;
                var color = Windows.UI.Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF);
                var halfColor = Windows.UI.Color.FromArgb((byte)(alpha / 2), 0xFF, 0xFF, 0xFF);

                // 直接更新所有 Tab 中的工具栏
                UpdateAllToolbarBackgrounds(color);

                // 更新 Tab 选中/悬停背景
                if (ImageTabView.Resources.ContainsKey("TabViewItemHeaderBackgroundSelected"))
                    ((SolidColorBrush)ImageTabView.Resources["TabViewItemHeaderBackgroundSelected"]).Color = color;
                if (ImageTabView.Resources.ContainsKey("TabViewItemHeaderBackgroundPointerOver"))
                    ((SolidColorBrush)ImageTabView.Resources["TabViewItemHeaderBackgroundPointerOver"]).Color = halfColor;

                tabLabel.Text = $"标签栏/工具栏透明度: {(int)args.NewValue}%";
                SettingsService.Current.ToolbarOpacityByte = alpha;
                SettingsService.Save();
            };
            settingsPanel.Children.Add(tabLabel);
            settingsPanel.Children.Add(tabSlider);

            // ── 加速加载开关 ──
            var fastLoadToggle = new ToggleSwitch
            {
                Header = "开启加速加载",
                OffContent = "关闭 (始终加载原图)",
                OnContent = "开启 (以屏幕分辨率预览，1:1时自动加载原图)",
                IsOn = _fastLoadEnabled,
                Margin = new Thickness(0, 12, 0, 0)
            };
            fastLoadToggle.Toggled += (s, args) =>
            {
                _fastLoadEnabled = fastLoadToggle.IsOn;
                SettingsService.Current.FastLoadEnabled = _fastLoadEnabled;
                SettingsService.Save();
            };
            settingsPanel.Children.Add(fastLoadToggle);

            var dialog = new ContentDialog
            {
                Title = "设置",
                Content = settingsPanel,
                CloseButtonText = "关闭",
                XamlRoot = this.Content.XamlRoot
            };
            await dialog.ShowAsync();
        }

        /// <summary>
        /// 遍历所有 Tab 更新工具栏背景色
        /// </summary>
        private void UpdateAllToolbarBackgrounds(Windows.UI.Color color)
        {
            var brush = new SolidColorBrush(color);
            foreach (var item in ImageTabView.TabItems)
            {
                if (item is TabViewItem tabItem && tabItem.Content is Grid contentGrid)
                {
                    foreach (var child in contentGrid.Children)
                    {
                        if (child is Grid g && g.Height == 40)
                        {
                            g.Background = new SolidColorBrush(color);
                        }
                    }
                }
            }
        }
        private void AddLoadRawButton(StackPanel panel, Image image, string filePath, bool isThumbnail, bool isRaw)
        {
             var loadRawBtn = new Button
            {
                Content = "渲染完整 RAW",
                Height = 32,
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x40, 0x00, 0x00, 0x00)), // 半透明背景
                BorderThickness = new Thickness(0),
                Tag = "LoadRawButton"
            };
            
            // 设置初始状态
            UpdateLoadRawButtonState(loadRawBtn, isRaw, isThumbnail);
            
            loadRawBtn.Click += async (s, e) => 
            {
                loadRawBtn.IsEnabled = false;
                loadRawBtn.Content = "正在渲染...";
                
                // 查找 LoadingOverlay (需向上查找 Grid)
                // 假设 Image 在 Viewbox 中，Viewbox 在 Grid 中
                if (image.Parent is FrameworkElement parent && parent.Parent is Grid grid)
                {
                     var loadingOverlay = grid.Children.FirstOrDefault(c => (c as FrameworkElement)?.Tag?.ToString() == "LoadingOverlay") as UIElement;
                     if (loadingOverlay != null) loadingOverlay.Visibility = Visibility.Visible;
                }
                
                await Task.Delay(10); // UI Refresh

                try
                {
                    var (fullSource, fw, fh, _) = await LoadImageSourceAsync(filePath, decodeWidthOverride: 0);
                    if (fullSource != null)
                    {
                        image.Source = fullSource;
                        var data = image.Tag as ImageTabData;
                        if (data != null)
                        {
                            data.ImageSource = fullSource;
                            data.OriginalWidth = fw;
                            data.OriginalHeight = fh;
                            data.IsFullResLoaded = true;
                        }
                        // 渲染完成后，按钮变为不可用（因为已经是 Full RAW 了）
                        UpdateLoadRawButtonState(loadRawBtn, true, false); 
                        loadRawBtn.Content = "渲染完整 RAW"; // 恢复文字
                    }
                }
                catch (Exception ex)
                {
                        ShowErrorDialog("渲染失败", ex.Message);
                        UpdateLoadRawButtonState(loadRawBtn, true, true); // 恢复可用
                        loadRawBtn.Content = "渲染完整 RAW";
                }
                finally
                {
                    if (image.Parent is FrameworkElement p && p.Parent is Grid g)
                    {
                        var loadingOverlay = g.Children.FirstOrDefault(c => (c as FrameworkElement)?.Tag?.ToString() == "LoadingOverlay") as UIElement;
                        if (loadingOverlay != null) loadingOverlay.Visibility = Visibility.Collapsed;
                    }
                }
            };
            
            panel.Children.Add(loadRawBtn);
        }

        private void UpdateLoadRawButtonState(Button btn, bool isRaw, bool isThumbnail)
        {
            if (isRaw)
            {
                if (isThumbnail)
                {
                    btn.IsEnabled = true;
                    btn.Opacity = 1.0;
                    btn.Content = "渲染完整 RAW";
                }
                else
                {
                    // 已经是 Full RAW
                    btn.IsEnabled = false;
                    btn.Opacity = 0.5;
                    btn.Content = "已加载完整 RAW";
                }
            }
            else
            {
                // 不是 RAW
                btn.IsEnabled = false;
                btn.Opacity = 0.5;
                btn.Content = "非 RAW 文件"; 
            }
        }


    }
}

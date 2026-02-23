using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using TransparentWinUI3.Services;
using WinRT.Interop; // For WindowNative

namespace TransparentWinUI3.Views
{
    public sealed partial class ColorManagedWindow : Window
    {
        private ColorManagementService _cmService;
        private string _imagePath;
        private string? _monitorProfilePath;
        private string? _customTargetPath;
        private string? _customSourcePath;
        private bool _isReady = false;
        private bool _imageLoaded = false;

        public ColorManagedWindow(string imagePath)
        {
            this.InitializeComponent();
            _imagePath = imagePath;
            _cmService = new ColorManagementService();

            // ParentHwnd is now assigned in Activated event since the handle might not be ready yet.
            // Setup simplified initialization
            this.Activated += ColorManagedWindow_Activated;
            
         }

        public async System.Threading.Tasks.Task UpdateImageAsync(string newPath)
        {
            if (_imagePath == newPath) return;
            
            _imagePath = newPath;
            LoadImage();
        }

        private async void LoadImage()
        {
            try
            {
                DebugInfoText.Text = $"Loading: {_imagePath}";
                await ImageControl.LoadImageAsync(_imagePath);
                
                // Update detected profile info
                string detectedName = ImageControl.DetectedSourceProfileName ?? "";
                System.Diagnostics.Debug.WriteLine($"[Window] Updating UI with detected profile: {detectedName} (Bytes: {ImageControl.DetectedSourceProfileBytes?.Length ?? 0})");

                // NEW: Check for Unified Metadata
                if (ImageControl.CurrentMetadata != null && ImageControl.CurrentMetadata.Description != "Default")
                {
                    string desc = ImageControl.CurrentMetadata.Description;
                    DetectedSourceProfileText.Text = $"检测到 ({ImageControl.CurrentMetadata.Transfer}): {desc}";
                    DetectedSourceProfileText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightGreen);
                    
                    // Set default curve selection based on metadata
                    if (ImageControl.CurrentMetadata.Transfer == Models.TransferFunction.PQ)
                    {
                        CurvePqRadio.IsChecked = true;
                    }
                    else
                    {
                        CurveGammaRadio.IsChecked = true;
                    }
                }
                else if (ImageControl.DetectedSourceProfileBytes != null || !string.IsNullOrEmpty(ImageControl.DetectedSourceProfileName))
                {
                    string desc = ImageControl.DetectedSourceProfileName ?? "Embedded Profile";
                    DetectedSourceProfileText.Text = $"检测到: {desc}";
                    DetectedSourceProfileText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightGreen);
                    
                    AutoSelectSourceSpace(desc);
                }
                else
                {
                    DetectedSourceProfileText.Text = "未检测到嵌入配置 (默认 sRGB)";
                    DetectedSourceProfileText.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);
                    SourceSpaceCombo.SelectedIndex = 0; // Default to sRGB
                }

                // FORCE UPDATE to apply whatever was detected/selected
                UpdateAllCMConfigs(null, null);
            }
            catch (Exception ex)
            {
                string error = $"Load Error: {ex.Message}";
                DebugInfoText.Text = error;
                System.Diagnostics.Debug.WriteLine($"[Window] [CRITICAL] LoadImage failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void AutoSelectSourceSpace(string desc)
        {
            string lowerDesc = desc.ToLowerInvariant();
            
            if (lowerDesc.Contains("srgb"))
            {
                SourceSpaceCombo.SelectedIndex = 0;
            }
            else if (lowerDesc.Contains("adobe rgb") || lowerDesc.Contains("argb"))
            {
                SourceSpaceCombo.SelectedIndex = 1;
            }
            else if (lowerDesc.Contains("display p3") || lowerDesc.Contains("p3"))
            {
                SourceSpaceCombo.SelectedIndex = 2;
            }
            else if (lowerDesc.Contains("prophoto"))
            {
                SourceSpaceCombo.SelectedIndex = 3;
            }
            else
            {
                // Custom or unknown
                SourceSpaceCombo.SelectedIndex = 4; // Custom (ICC)
                _customSourcePath = null; // We use DetectedSourceProfileBytes in ImageControl
            }
        }

        private void ColorManagedWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                _isReady = true;
                
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                ImageControl.ParentHwnd = hwnd;

                UpdateMonitorProfile();
                UpdateHdrStatus();

                // Only load the image once after window is active
                if (!_imageLoaded)
                {
                    _imageLoaded = true;
                    LoadImage();
                }
            }
        }

        private void UpdateHdrStatus()
        {
            try
            {
                IntPtr hwnd = WindowNative.GetWindowHandle(this);
                bool isHdr = _cmService.IsHdrDisplayActive(hwnd);
                
                HdrStatusText.Text = isHdr ? "显示器 HDR: Active (BT.2020 PQ)" : "显示器 HDR: Off (SDR)";
                HdrStatusText.Foreground = isHdr ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightGreen) 
                                                 : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);

                // Show butten if image is HDR but display is SDR
                OpenHdrSettingsBtn.Visibility = (!isHdr && ImageControl.IsHdrSource) ? Visibility.Visible : Visibility.Collapsed;

                ImageHdrText.Text = ImageControl.IsHdrSource ? $"图像 HDR: Yes ({ImageControl.CurrentMetadata.Description})" 
                                                             : $"图像 HDR: SDR ({ImageControl.CurrentMetadata.Description})";
                ImageHdrText.Foreground = ImageControl.IsHdrSource ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Orange)
                                                                 : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray);

                MappingModeText.Text = $"管线模式: {ImageControl.CMDebugInfo}";
                
                                System.Diagnostics.Debug.WriteLine($"[Window] HDR UI Updated. Display: {isHdr}, Image: {ImageControl.IsHdrSource}, Mode: {ImageControl.CMDebugInfo}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Window] [ERROR] HDR status update failed: {ex.Message}");
            }
        }

        private void HdrIntensitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (ImageControl == null || !_isReady) return;
            
            ImageControl.HdrIntensity = (float)e.NewValue;
            if (HdrIntensityValueText != null)
                HdrIntensityValueText.Text = e.NewValue.ToString("F1") + "x";
            
            // Re-apply CM to update the pipeline
            UpdateAllCMConfigs(null, null);
        }

        private void ForceLinearCheckbox_Toggled(object sender, RoutedEventArgs e)
        {
            if (ImageControl == null || !_isReady) return;
            ImageControl.ForceLinearGamma = (ForceLinearCheckbox.IsChecked == true);
            UpdateAllCMConfigs(null, null);
        }

        private void LimitedRangeCheckbox_Toggled(object sender, RoutedEventArgs e)
        {
            if (ImageControl == null || !_isReady) return;
            ImageControl.IsLimitedRange = (LimitedRangeCheckbox.IsChecked == true);
            UpdateAllCMConfigs(null, null);
        }

        private void CurveOption_Checked(object sender, RoutedEventArgs e)
        {
            if (ImageControl == null || !_isReady) return;
            
            if (CurvePqRadio != null && CurvePqRadio.IsChecked == true)
            {
                ImageControl.IsDataPQ = true;
                if (PqControlsPanel != null) PqControlsPanel.Visibility = Visibility.Visible;
            }
            else
            {
                ImageControl.IsDataPQ = false;
                if (PqControlsPanel != null) PqControlsPanel.Visibility = Visibility.Collapsed;
            }
            UpdateAllCMConfigs(null, null);
        }

        private void PqNitsSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (ImageControl == null || !_isReady) return;
            
            ImageControl.PqMaxNits = (float)e.NewValue;
            if (PqNitsValueText != null)
                PqNitsValueText.Text = $"{e.NewValue:F0} nits";
            
            UpdateAllCMConfigs(null, null);
        }

        private async void OpenHdrSettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Open advanced color settings directly
                await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:display-advancedcolor"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Window] Failed to launch HDR settings: {ex.Message}");
            }
        }

        private void UpdateMonitorProfile()
        {
            try
            {
                var hwnd = WindowNative.GetWindowHandle(this);
                string? profilePath = _cmService.GetMonitorICCProfile(hwnd);
                
                System.Diagnostics.Debug.WriteLine($"[Window] Detected Monitor Profile: {profilePath ?? "null"}");

                if (!string.IsNullOrEmpty(profilePath))
                {
                    _monitorProfilePath = profilePath;
                    MonitorProfileText.Text = System.IO.Path.GetFileName(profilePath) + " (Applied)";
                }
                else
                {
                    _monitorProfilePath = null;
                    MonitorProfileText.Text = "System Default (sRGB)";
                }

                UpdateAllCMConfigs(null, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Window] [ERROR] UpdateMonitorProfile failed: {ex.Message}");
                DebugInfoText.Text = $"Error: {ex.Message}";
            }
        }

        private void EnableCMToggle_Toggled(object sender, RoutedEventArgs e)
        {
             if (EnableCMToggle == null || ImageControl == null) return;
             
             // IsCmDisabled = !IsOn
             // If Toggle is ON (Enabled), IsCmDisabled is FALSE.
             ImageControl.IsCmDisabled = !EnableCMToggle.IsOn;
             
             UpdateAllCMConfigs(null, null);
        }

        private void StrictDebugCheck_Changed(object sender, RoutedEventArgs e)
        {
             if (ImageControl == null) return;
             ImageControl.IsStrictDebugMode = StrictDebugModeCheck.IsChecked == true;
             UpdateAllCMConfigs(null, null);
        }

        private void RenderingIntentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ImageControl == null || !_isReady) return;
            UpdateAllCMConfigs(null, null);
        }

        private async void UpdateAllCMConfigs(object? sender, SelectionChangedEventArgs? e)
        {
            if (!_isReady || ImageControl == null) return;

            if (!EnableCMToggle.IsOn)
            {
                await ImageControl.ConfigureColorManagementAsync(null, null, 0);
                DebugInfoText.Text = "Color Management Disabled";
                return;
            }

            // 1. Resolve Target
            string? targetPath = null;
            if (TargetSpaceCombo != null)
            {
                int idx = TargetSpaceCombo.SelectedIndex;
                switch (idx)
                {
                    case 0: // Auto-Monitor
                        targetPath = _monitorProfilePath ?? _cmService.GetStandardSRGBPath();
                        LoadTargetProfileBtn.Visibility = Visibility.Collapsed;
                        break;
                    case 1: // sRGB
                        targetPath = _cmService.GetStandardSRGBPath();
                        LoadTargetProfileBtn.Visibility = Visibility.Collapsed;
                        break;
                    case 2: // Adobe RGB
                        targetPath = ResolveStandardProfilePath("AdobeRGB1998.icc");
                        LoadTargetProfileBtn.Visibility = Visibility.Collapsed;
                        break;
                    case 3: // Display P3
                        targetPath = ResolveStandardProfilePath("Display P3.icc");
                        LoadTargetProfileBtn.Visibility = Visibility.Collapsed;
                        break;
                    case 4: // ProPhoto
                        targetPath = ResolveStandardProfilePath("ProPhoto-v4.icc");
                        LoadTargetProfileBtn.Visibility = Visibility.Collapsed;
                        break;
                    case 5: // Custom
                        targetPath = _customTargetPath;
                        LoadTargetProfileBtn.Visibility = Visibility.Visible;
                        break;
                }
            }

            // 2. Resolve Source
            string? sourcePath = null;
            if (SourceSpaceCombo != null)
            {
                int idx = SourceSpaceCombo.SelectedIndex;
                switch (idx)
                {
                    case 0: // sRGB
                        sourcePath = _cmService.GetStandardSRGBPath();
                        LoadSourceProfileBtn.Visibility = Visibility.Collapsed;
                        break;
                    case 1: // Adobe RGB
                        sourcePath = ResolveStandardProfilePath("AdobeRGB1998.icc");
                        LoadSourceProfileBtn.Visibility = Visibility.Collapsed;
                        break;
                    case 2: // Display P3
                        sourcePath = ResolveStandardProfilePath("Display P3.icc");
                        LoadSourceProfileBtn.Visibility = Visibility.Collapsed;
                        break;
                    case 3: // ProPhoto
                        sourcePath = ResolveStandardProfilePath("ProPhoto-v4.icc");
                        LoadSourceProfileBtn.Visibility = Visibility.Collapsed;
                        break;
                    case 4: // Custom
                        sourcePath = _customSourcePath;
                        LoadSourceProfileBtn.Visibility = Visibility.Visible;
                        break;
                    case 5: // Diagnostic Red
                        sourcePath = "DIAGNOSTIC_RED";
                        LoadSourceProfileBtn.Visibility = Visibility.Collapsed;
                        break;
                    case 6: // Force Gamma
                        sourcePath = "FORCE_GAMMA";
                        LoadSourceProfileBtn.Visibility = Visibility.Collapsed;
                        break;
                }
            }

            // 3. Resolve Intent
            uint intent = 0;
            if (RenderingIntentCombo != null)
            {
                intent = (uint)RenderingIntentCombo.SelectedIndex;
            }

            // 4. Existence and Warning Checks
            string debugSrc = sourcePath == "DIAGNOSTIC_RED" ? "DIAGNOSTIC" : (sourcePath == "FORCE_GAMMA" ? "MATH_TEST" : (string.IsNullOrEmpty(sourcePath) ? "None" : System.IO.Path.GetFileName(sourcePath)));
            string debugDst = string.IsNullOrEmpty(targetPath) ? "None" : System.IO.Path.GetFileName(targetPath);
            
            string debugMsg = $"Pipe: {debugSrc} -> {debugDst}";

            if (sourcePath != "DIAGNOSTIC_RED" && sourcePath != "FORCE_GAMMA" && !string.IsNullOrEmpty(sourcePath) && !System.IO.File.Exists(sourcePath))
            {
                debugMsg = $"[ERROR] Source Profile NOT FOUND: {debugSrc}";
            }
            else if (!string.IsNullOrEmpty(targetPath) && !System.IO.File.Exists(targetPath))
            {
                debugMsg = $"[ERROR] Target Profile NOT FOUND: {debugDst}";
            }
            
            if (sourcePath == "DIAGNOSTIC_RED") debugMsg += "\n[MODE] Visual Pipe Test (Red)";
            if (sourcePath == "FORCE_GAMMA") debugMsg += "\n[MODE] Force Gamma Shift (Math)";

            DebugInfoText.Text = debugMsg;
            System.Diagnostics.Debug.WriteLine($"[CM] {debugMsg}");

            await ImageControl.ConfigureColorManagementAsync(sourcePath, targetPath, intent);
            
            // Show result
            DebugInfoText.Text = debugMsg + "\n" + ImageControl.CMDebugInfo;
            UpdateHdrStatus();
        }

        private string? ResolveStandardProfilePath(string filename)
        {
            // 1. Try local project 'icc' folder first (User's preferred location)
            string localIccFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icc");
            string localPath = System.IO.Path.Combine(localIccFolder, filename);
            if (System.IO.File.Exists(localPath)) return localPath;

            // Fallback for Debug mode where base might be deep in bin
            string projectIccFolder = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "icc");
            string projectPath = System.IO.Path.Combine(projectIccFolder, filename);
            if (System.IO.File.Exists(projectPath)) return projectPath;

            // 2. Windows standard color folder fallback
            string sysFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "spool\\drivers\\color");
            string sysPath = System.IO.Path.Combine(sysFolder, filename);
            if (System.IO.File.Exists(sysPath)) return sysPath;

            // Try common variants in system folder
            if (filename == "AdobeRGB1998.icc") 
            {
                string p2 = System.IO.Path.Combine(sysFolder, "Adobe RGB (1998).icm");
                if (System.IO.File.Exists(p2)) return p2;
            }

            return null;
        }

        private async void LoadSourceProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".icc");
            picker.FileTypeFilter.Add(".icm");
            var hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                _customSourcePath = file.Path;
                UpdateAllCMConfigs(null, null);
            }
        }

        private void RefreshMonitorBtn_Click(object sender, RoutedEventArgs e)
        {
            UpdateMonitorProfile();
        }

        private async void LoadProfileBtn_Click(object sender, RoutedEventArgs e)
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add(".icc");
            picker.FileTypeFilter.Add(".icm");

            // WinUI 3 Window Handle hack
            var hwnd = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                try
                {
                    // Update UI text
                    _customTargetPath = file.Path;
                    MonitorProfileText.Text = file.Name + " (Custom)";
                    DebugInfoText.Text = $"Custom Target Profile: {file.Path}";
                    
                    UpdateAllCMConfigs(null, null);
                }
                catch (Exception ex)
                {
                    DebugInfoText.Text = $"Error loading custom profile: {ex.Message}";
                }
            }
        }
    }
}

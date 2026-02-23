using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TransparentWinUI3
{
    public sealed partial class GpuDebugDialog : ContentDialog, INotifyPropertyChanged
    {
        private string _imageWidth = "0";
        private string _imageHeight = "0";
        private int _patternIndex;
        private string _logText = "";

        public string ImageWidth
        {
            get => _imageWidth;
            set { _imageWidth = value; OnPropertyChanged(); }
        }

        public string ImageHeight
        {
            get => _imageHeight;
            set { _imageHeight = value; OnPropertyChanged(); }
        }

        // Helper to get int values
        public int GetWidthInt() => int.TryParse(_imageWidth, out int v) ? v : 0;
        public int GetHeightInt() => int.TryParse(_imageHeight, out int v) ? v : 0;
        public void SetWidthInt(int v) => ImageWidth = v.ToString();
        public void SetHeightInt(int v) => ImageHeight = v.ToString();

        public int PatternIndex
        {
            get => _patternIndex;
            set { _patternIndex = value; OnPropertyChanged(); }
        }

        public string LogText
        {
            get => _logText;
            set { _logText = value; OnPropertyChanged(); }
        }

        public GpuDebugDialog()
        {
            this.InitializeComponent();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

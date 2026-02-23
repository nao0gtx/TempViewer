using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace TransparentWinUI3.Services
{
    public class AppSettings
    {
        public bool FastLoadEnabled { get; set; } = true;
        public int ScreenDecodeWidth { get; set; } = 1920;
        public byte ToolbarOpacityByte { get; set; } = 0x18;
        public Dictionary<string, string> CustomSettings { get; set; } = new Dictionary<string, string>();
    }

    public static class SettingsService
    {
        private static readonly string SettingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        private static AppSettings _current = new AppSettings();

        public static AppSettings Current => _current;

        static SettingsService()
        {
            Load();
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    _current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings] Failed to load settings: {ex.Message}");
                _current = new AppSettings();
            }
        }

        public static void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Settings] Failed to save settings: {ex.Message}");
            }
        }
    }
}

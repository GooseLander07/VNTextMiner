using System;
using System.IO;
using System.Text.Json;
using OverlayApp.Models;

namespace OverlayApp.Services
{
    public static class ConfigService
    {
        private static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VNTextMiner",
            "config.json");

        public static AppConfig Current { get; private set; } = new AppConfig();

        public static void Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    Current = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                }
            }
            catch
            {
                // If load fails, stick to defaults
                Current = new AppConfig();
            }
        }

        public static void Save()
        {
            try
            {
                // Ensure directory exists
                string? dir = Path.GetDirectoryName(ConfigPath);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch { /* Ignore save errors for now */ }
        }
    }
}
// League Account Manager
// Copyright (c) 2026 Tuc2300. All rights reserved.
// Licensed under the BSD 3-Clause License: https://github.com/Tuc2300/LeagueAccountManager/blob/main/LICENSE

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Accountmanager
{
    public class AppSettings
    {
        [JsonPropertyName("keyFilePath")]
        public string KeyFilePath { get; set; } = "";

        [JsonPropertyName("migratedFromDpapi")]
        public bool MigratedFromDpapi { get; set; } = false;

        [JsonIgnore]
        private static string SettingsPath => Path.Combine(
            System.Windows.Forms.Application.StartupPath, "settings.json");

        public static AppSettings Load()
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();
            try
            {
                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public void Save()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, options));
        }
    }
}

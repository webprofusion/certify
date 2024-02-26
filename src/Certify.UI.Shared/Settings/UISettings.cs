using System.IO;
using Certify.Models;

namespace Certify.UI.Settings
{
    public class UISettings
    {
        private const string SETTINGS_FILE = "ui.json";

        public double? Width { get; set; }
        public double? Height { get; set; }

        public double? Left { get; set; }
        public double? Top { get; set; }

        public string UITheme { get; set; }

        public double? Scaling { get; set; } = 1;

        public string PreferredUICulture { get; set; } = "en-US";

        public static UISettings Load()
        {
            var uiSettingsFilePath = Path.Combine(EnvironmentUtil.CreateAppDataPath(), SETTINGS_FILE);
            if (File.Exists(uiSettingsFilePath))
            {
                try
                {
                    var configData = File.ReadAllText(uiSettingsFilePath);
                    var uiSettings = Newtonsoft.Json.JsonConvert.DeserializeObject<UISettings>(configData);

                    return uiSettings;
                }
                catch
                {
                    // if setting fail to load (permission etc) we will use defaults
                }
            }

            return null;
        }

        public static void Save(UISettings uiSettings)
        {
            try
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(uiSettings, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(Path.Combine(EnvironmentUtil.CreateAppDataPath(), SETTINGS_FILE), json);
            }
            catch { }
        }
    }
}

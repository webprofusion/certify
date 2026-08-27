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

        public string CommunityMode { get; set; }

        /// <summary>
        /// UTC time the evaluation mode reminder was last shown to the user
        /// </summary>
        public System.DateTime? LastEvaluationMsgUtc { get; set; }

        /// <summary>
        /// UTC time the app was first run by this user, used to defer evaluation mode reminders for new installs
        /// </summary>
        public System.DateTime? FirstRunUtc { get; set; }

        public static UISettings Load()
        {
            var uiSettingsFilePath = Path.Combine(EnvironmentUtil.EnsuredAppDataPath(), SETTINGS_FILE);
            if (File.Exists(uiSettingsFilePath))
            {
                try
                {
                    var configData = File.ReadAllText(uiSettingsFilePath);
                    var uiSettings = Newtonsoft.Json.JsonConvert.DeserializeObject<UISettings>(configData);

                    if (uiSettings != null && uiSettings.FirstRunUtc == null)
                    {
                        // settings predate first run tracking, approximate using the age of the settings file itself
                        try
                        {
                            uiSettings.FirstRunUtc = File.GetCreationTimeUtc(uiSettingsFilePath);
                        }
                        catch
                        {
                            // if we can't determine the file age, first run will be set to the current time by the caller
                        }
                    }

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
                File.WriteAllText(Path.Combine(EnvironmentUtil.EnsuredAppDataPath(), SETTINGS_FILE), json);
            }
            catch { }
        }
    }
}

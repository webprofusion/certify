using Certify.Locales;
using Certify.Management;
using Certify.Models;
using Certify.Models.Config;
using Certify.Shared.Utils;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace Certify.UI.Controls.ManagedCertificate
{
    /// <summary>
    /// Interaction logic for ManagedCertificateSettingsScriptHooks.xaml 
    /// </summary>
    public partial class ScriptHooks : UserControl
    {
        protected Certify.UI.ViewModel.ManagedCertificateViewModel ItemViewModel => UI.ViewModel.ManagedCertificateViewModel.Current;

        public ScriptHooks()
        {
            InitializeComponent();

            LoadScriptPresets();
        }

        private string GetPresetScriptPath()
        {
            return Environment.CurrentDirectory + "\\scripts\\common";
        }

        private void LoadScriptPresets()
        {
            try
            {
                var path = GetPresetScriptPath() + "\\index.json";
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var index = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Certify.Models.Config.ScriptPreset>>(json);
                    this.PresetScripts.ItemsSource = index;
                }
            }
            catch (Exception) { }
        }

        private void FileBrowse_Click(object sender, EventArgs e)
        {
            var button = (Button)sender;
            var config = ItemViewModel.SelectedItem.RequestConfig;
            var dialog = new OpenFileDialog()
            {
                Filter = "Powershell Scripts (*.ps1)| *.ps1;"
            };
            Action saveAction = null;
            string filename = "";
            if (button.Name == "Button_PreRequest")
            {
                filename = config.PreRequestPowerShellScript;
                saveAction = () => config.PreRequestPowerShellScript = dialog.FileName;
            }
            else if (button.Name == "Button_PostRequest")
            {
                filename = config.PostRequestPowerShellScript;
                saveAction = () => config.PostRequestPowerShellScript = dialog.FileName;
            }
            try
            {
                var fileInfo = new FileInfo(filename);
                if (fileInfo.Directory.Exists)
                {
                    dialog.InitialDirectory = fileInfo.Directory.FullName;
                    dialog.FileName = fileInfo.Name;
                }
            }
            catch (ArgumentException)
            {
                // invalid file passed in, open dialog with default options
            }
            if (dialog.ShowDialog() == true)
            {
                saveAction();
            }
        }

        private async void TestScript_Click(object sender, EventArgs e)
        {
            var button = (Button)sender;
            string scriptFile = null;
            var result = new CertificateRequestResult { ManagedItem = ItemViewModel.SelectedItem, IsSuccess = true, Message = "Script Testing Message" };
            if (button.Name == "Button_TestPreRequest")
            {
                scriptFile = ItemViewModel.SelectedItem.RequestConfig.PreRequestPowerShellScript;
                result.IsSuccess = false; // pre-request messages will always have IsSuccess = false
            }
            else if (button.Name == "Button_TestPostRequest")
            {
                scriptFile = ItemViewModel.SelectedItem.RequestConfig.PostRequestPowerShellScript;
            }
            if (string.IsNullOrEmpty(scriptFile)) return; // don't try to run empty script
            try
            {
                string scriptOutput = await PowerShellManager.RunScript(result, scriptFile);
                MessageBox.Show(scriptOutput, "Powershell Output", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(ex.Message, "Script Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void TestWebhook_Click(object sender, EventArgs e)
        {
            try
            {
                Button_TestWebhook.IsEnabled = false;

                var config = ItemViewModel.SelectedItem.RequestConfig;
                if (!Uri.TryCreate(config.WebhookUrl, UriKind.Absolute, out var result))
                {
                    MessageBox.Show($"The webhook url must be a valid url.", SR.ManagedCertificateSettings_WebhookTestFailed, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                if (string.IsNullOrEmpty(config.WebhookMethod))
                {
                    MessageBox.Show($"The webhook method must be selected.", SR.ManagedCertificateSettings_WebhookTestFailed, MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                bool forSuccess = config.WebhookTrigger == Webhook.ON_SUCCESS;
                var webhookResult = await Webhook.SendRequest(config, forSuccess);
                string completed = webhookResult.Success ? SR.succeed : SR.failed;
                MessageBox.Show(string.Format(SR.ManagedCertificateSettings_WebhookRequestResult, completed, webhookResult.StatusCode), SR.ManagedCertificateSettings_WebhookTest, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(SR.ManagedCertificateSettings_WebhookRequestError, ex.Message), SR.ManagedCertificateSettings_WebhookTestFailed, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                Button_TestWebhook.IsEnabled = true;
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            // get selected preset script
            var button = e.Source as System.Windows.Controls.Button;
            var config = ItemViewModel.SelectedItem.RequestConfig;

            if (button.CommandParameter != null)
            {
                ScriptPreset preset = button.CommandParameter as ScriptPreset;
                string presetFilePath = GetPresetScriptPath() + "\\" + preset.File;
                if (preset.Language == "PowerShell")
                {
                    if (preset.Usage == "PreRequest")
                    {
                        config.PreRequestPowerShellScript = presetFilePath;
                    }
                    if (preset.Usage == "PostRequest")
                    {
                        config.PostRequestPowerShellScript = presetFilePath;
                    }
                }
            }
        }
    }
}

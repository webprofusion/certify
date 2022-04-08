using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;

using Certify.Config.Migration;
using Certify.Models;
using Certify.UI.Shared;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace Certify.UI.Windows
{
    /// <summary>
    /// Interaction logic for ImportExport.xaml
    /// </summary>
    public partial class ImportExport
    {
        public Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;

        public class ImportExportModel : BindableBase
        {
            public bool InProgress { get; set; }
            public bool IsImportReady { get; set; }
            public bool IsPreviewReady { get; set; }
            public ManagedCertificateFilter Filter { get; set; } = new ManagedCertificateFilter { };
            public ImportSettings ImportSettings { get; set; } = new ImportSettings { };
            public ExportSettings ExportSettings { get; set; } = new ExportSettings { };
            public ImportExportPackage Package { get; set; }
        }

        public ImportExportModel Model { get; set; } = new ImportExportModel();
        public ImportExport()
        {
            InitializeComponent();

            DataContext = Model;
        }

        private async void Import_Click(object sender, RoutedEventArgs e)
        {
            Model.IsPreviewReady = false;
            Model.IsImportReady = false;

            var dialog = new OpenFileDialog();

            dialog.DefaultExt = "json";
            dialog.Filter = "Json files (*.json)|*.json|All files (*.*)|*.*";

            var isPreview = true;

            // prompt user for save file location and perform export to json file

            if (dialog.ShowDialog() == true)
            {
                var filePath = dialog.FileName;

                var json = System.IO.File.ReadAllText(filePath);
                try
                {
                    Model.Package = JsonConvert.DeserializeObject<ImportExportPackage>(json);
                }
                catch (Exception)
                {
                    MessageBox.Show("The selected file could not be read as valid Import Package.");
                    return;
                }

                Model.ImportSettings.EncryptionSecret = txtSecret.Password;
                Model.InProgress = true;

                var results = await MainViewModel.PerformSettingsImport(Model.Package, Model.ImportSettings, isPreview);

                PrepareImportSummary(isPreview, results);
                Model.InProgress = false;
            }
        }

        private void PrepareImportSummary(bool isPreview, List<ActionStep> results)
        {
            if (!isPreview && results.All(r => r.HasError == false))
            {
                MainViewModel.ShowNotification("Import completed OK", Shared.NotificationType.Success);
            }

            PrepareImportPreview(Model.Package, results, isPreview ? "Import Preview" : "Import Results");

            if (results.All(r => r.HasError == false))
            {
                Model.IsImportReady = true;
                Model.IsPreviewReady = true;
            }
            else
            {
                Model.IsImportReady = false;
                Model.IsPreviewReady = true;
            }
        }

        private async void CompleteImport_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Are you sure you wish to perform the import as shown in the preview? The import cannot be reverted once complete.", "Perform Import?", MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes)
            {
                Model.InProgress = true;
                var results = await MainViewModel.PerformSettingsImport(Model.Package, Model.ImportSettings, false);

                PrepareImportSummary(false, results);
                Model.InProgress = false;
            }
        }

        private void PrepareImportPreview(ImportExportPackage package, List<ActionStep> steps, string title)
        {
            Markdig.MarkdownPipeline _markdownPipeline;
            var _css = "";

            var _markdownPipelineBuilder = new Markdig.MarkdownPipelineBuilder();
            _markdownPipelineBuilder.Extensions.Add(new Markdig.Extensions.Tables.PipeTableExtension());
            _markdownPipeline = _markdownPipelineBuilder.Build();

            try
            {
                var cssPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Assets", "CSS", "markdown.css");
                _css = System.IO.File.ReadAllText(cssPath);

                if (MainViewModel.UISettings?.UITheme?.ToLower() == "dark")
                {
                    cssPath = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Assets", "CSS", "dark-mode.css");
                    _css += System.IO.File.ReadAllText(cssPath);
                }
            }
            catch
            {

            }

            var intro = $"Importing from source: {package.SourceName}, exported {package.ExportDate.ToLongDateString()}, app version {package.SystemVersion.ToString().AsNullWhenBlank() ?? "(unknown)"}";
            var markdown = GetStepsAsMarkdown(steps, title, intro);

            var result = Markdig.Markdown.ToHtml(markdown, _markdownPipeline);
            result = "<html><head><meta http-equiv='Content-Type' content='text/html;charset=UTF-8'><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\" />" +
                    "<style>" + _css + "</style></head><body>" + result + "</body></html>";

            MarkdownView.NavigateToString(result);
        }

        private string GetStepsAsMarkdown(IEnumerable<ActionStep> steps, string title, string intro)
        {
            //TODO: deduplicate this vs. Preview version
            var newLine = "\r\n";
            var warningSymbol = "⚠️";
            var errorSymbol = "🛑";

            var sb = new StringBuilder();

            if (title != null)
            {
                sb.AppendLine("# " + title);
            }

            if (intro != null)
            {
                sb.AppendLine(intro);
            }

            foreach (var s in steps)
            {
                var statusSymbol = "";
                if (s.Substeps?.Any(t => t.HasWarning) == true)
                {
                    statusSymbol = warningSymbol;
                }

                if (s.Substeps?.Any(t => t.HasError) == true)
                {
                    statusSymbol = errorSymbol;
                }

                sb.AppendLine("_____");

                sb.AppendLine(newLine + "## " + s.Title + " " + statusSymbol);

                if (!string.IsNullOrEmpty(s.Description))
                {
                    sb.AppendLine(s.Description);
                }

                if (s.Substeps != null)
                {

                    foreach (var sub in s.Substeps)
                    {
                        var stepSymbol = "";
                        if (sub.HasWarning)
                        {
                            stepSymbol = " " + warningSymbol;
                        }

                        if (sub.HasError)
                        {
                            stepSymbol = " " + errorSymbol;
                        }

                        if (!string.IsNullOrEmpty(sub.Title) && !string.IsNullOrEmpty(sub.Description) && sub.Title != s.Title && sub.Title != sub.Description)
                        {
                            sb.AppendLine(newLine + "### " + sub.Title);
                        }

                        if (!string.IsNullOrEmpty(sub.Description))
                        {
                            if (sub.Description.Contains("|"))
                            {
                                // table items
                                sb.AppendLine(sub.Description + stepSymbol);
                            }
                            else if (sub.Description.StartsWith("\r\n"))
                            {
                                sb.AppendLine(sub.Description + stepSymbol);
                            }
                            else
                            {
                                // list items
                                sb.AppendLine(" - " + sub.Description + stepSymbol);
                            }
                        }
                        else
                        {
                            sb.AppendLine(" - " + sub.Title + stepSymbol);
                        }
                    }
                }
            }

            return sb.ToString();
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            var filter = new ManagedCertificateFilter { };
            var settings = new ExportSettings { };

            var dialog = new SaveFileDialog();

            // prompt user for save file location and perform export to json file
            dialog.FileName = $"certifytheweb_export_{DateTime.Now.ToString("yyyyMMdd")}.json";
            dialog.DefaultExt = "json";
            dialog.Filter = "Json files (*.json)|*.json|All files (*.*)|*.*";

            if (dialog.ShowDialog() == true)
            {
                var savePath = dialog.FileName;

                settings.EncryptionSecret = txtSecret.Password;
                var export = await MainViewModel.GetSettingsExport(filter, settings, false);

                var json = JsonConvert.SerializeObject(export, new JsonSerializerSettings { Formatting = Formatting.Indented, NullValueHandling = NullValueHandling.Ignore });
                System.IO.File.WriteAllText(savePath, json);

                MainViewModel.ShowNotification("Export completed OK", NotificationType.Success);
            }
        }
    }
}

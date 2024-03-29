﻿using System.IO;
using System.Text;
using System.Threading.Tasks;
using Certify.Models;

namespace Certify.UI.Windows
{
    public partial class UpdateAvailable
    {
        protected Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;

        private Markdig.MarkdownPipeline _markdownPipeline;
        private string _css = "";
        private Models.UpdateCheck _update;
        public UpdateAvailable(Models.UpdateCheck update = null)
        {
            InitializeComponent();

            DataContext = MainViewModel;

            Width *= MainViewModel.UIScaleFactor;
            Height *= MainViewModel.UIScaleFactor;

            var _markdownPipelineBuilder = new Markdig.MarkdownPipelineBuilder();
            _markdownPipelineBuilder.Extensions.Add(new Markdig.Extensions.Tables.PipeTableExtension());
            _markdownPipeline = _markdownPipelineBuilder.Build();
            try
            {
                var cssPath = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Assets", "CSS", "markdown.css");
                _css = System.IO.File.ReadAllText(cssPath);

                if (MainViewModel.UISettings?.UITheme?.ToLower() == "dark")
                {
                    cssPath = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "Assets", "CSS", "dark-mode.css");
                    _css += System.IO.File.ReadAllText(cssPath);
                }
            }
            catch
            {
                // will fail in design mode
            }

            _update = update;

        }
        private async Task<string> UpdatePreview()
        {

            if (_update == null)
            {
                return "Update information unavailable. Do not proceed with the update until this is resolved.";
            }

            // generate release notes

            var showAllChanges = false;

            UpdateMessage.Text = _update.Message.Body;
            CurrentVersionInfo.Text = "Current installed version: " + _update.InstalledVersion.ToString();

            var sb = new StringBuilder();
            sb.AppendLine("**Release Notes**\n");

            sb.AppendLine($"{_update.Message.ReleaseNotesURL}\n");

            if (_update.Message.ReleaseNotes != null)
            {

                foreach (var note in _update.Message.ReleaseNotes)
                {
                    if (showAllChanges || _update.InstalledVersion == null || AppVersion.IsOtherVersionNewer(_update.InstalledVersion, AppVersion.FromString(note.Version)))
                    {
                        sb.AppendLine($"{note.Version}: {note.ReleaseDate}");
                        sb.AppendLine($"{note.Body}\n");
                        sb.AppendLine($"-------------------------\n");
                    }
                }
            }

            var result = Markdig.Markdown.ToHtml(sb.ToString(), _markdownPipeline);
            result = "<html><head><meta http-equiv='Content-Type' content='text/html;charset=UTF-8'><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\" />" +
                    "<style>" + _css + "</style></head><body>" + result + "</body></html>";

            return await Task.FromResult(result);
        }

        private async void UserControl_IsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {

                var result = await UpdatePreview();
                MarkdownView.NavigateToString(result);

            }
        }

        private void Cancel_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Proceed_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}

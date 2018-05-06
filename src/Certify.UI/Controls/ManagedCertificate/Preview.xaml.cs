using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Controls;
using Certify.Models;

namespace Certify.UI.Controls.ManagedCertificate
{
    /// <summary>
    /// Interaction logic for Preview.xaml 
    /// </summary>
    public partial class Preview : UserControl
    {
        protected Certify.UI.ViewModel.ManagedCertificateViewModel ItemViewModel => UI.ViewModel.ManagedCertificateViewModel.Current;
        protected Certify.UI.ViewModel.AppViewModel AppViewModel => UI.ViewModel.AppViewModel.Current;

        private ObservableCollection<ActionStep> Steps { get; set; }

        private Markdig.MarkdownPipeline _markdownPipeline;
        private string _css = "";

        public Preview()
        {
            InitializeComponent();

            Steps = new ObservableCollection<ActionStep>();

            var _markdownPipelineBuilder = new Markdig.MarkdownPipelineBuilder();
            _markdownPipelineBuilder.Extensions.Add(new Markdig.Extensions.Tables.PipeTableExtension());
            _markdownPipeline = _markdownPipelineBuilder.Build();
            _css = System.IO.File.ReadAllText(System.AppDomain.CurrentDomain.BaseDirectory + "\\Assets\\CSS\\markdown.css");
        }

        private async Task UpdatePreview()
        {
            // generate preview
            if (ItemViewModel.SelectedItem != null)
            {
                var loadingMsg = "<html><head><meta http-equiv='Content-Type' content='text/html;charset=UTF-8'><style>" + _css + "</style></head><body>Generating Preview..</body></html>";
                List<ActionStep> steps = new List<ActionStep>();
                try
                {
                    ItemViewModel.UpdateManagedCertificateSettings(throwOnInvalidSettings: false);

                    steps = await AppViewModel.GetPreviewActions(ItemViewModel.SelectedItem);
                }
                catch (Exception exp)
                {
                    steps.Add(new ActionStep { Title = "Could not generate preview", Description = $"A problem occurred generating the preview: {exp.Message}" });
                }

                Steps = new ObservableCollection<ActionStep>(steps);

                App.Current.Dispatcher.Invoke((Action)delegate
                {
                    string markdown = GetStepsAsMarkdown(Steps);

                    var result = Markdig.Markdown.ToHtml(markdown, _markdownPipeline);
                    result = "<html><head><meta http-equiv='Content-Type' content='text/html;charset=UTF-8'><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\" />" +
                            "<style>" + _css + "</style></head><body>" + result + "</body></html>";
                    MarkdownView.NavigateToString(result);
                });
            }
        }

        private string GetStepsAsMarkdown(IEnumerable<ActionStep> steps)
        {
            var markdownText = "";
            var newLine = "\r\n";
            foreach (var s in Steps)
            {
                markdownText += newLine + "# " + s.Title + newLine;
                markdownText += s.Description;

                if (s.Substeps != null)
                {
                    foreach (var sub in s.Substeps)
                    {
                        markdownText += sub.Description + newLine;
                    }
                }
            }
            return markdownText;
        }

        private void UserControl_IsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            if (this.IsVisible)
            {
                Task.Run(() => UpdatePreview());
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
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
            try
            {
                _css = System.IO.File.ReadAllText(System.AppDomain.CurrentDomain.BaseDirectory + "\\Assets\\CSS\\markdown.css");
            }
            catch
            {
                // will fail in design mode
            }

        }

        private async Task<string> UpdatePreview()
        {
            // generate preview
            if (ItemViewModel.SelectedItem != null)
            {

                var steps = new List<ActionStep>();
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


                var markdown = GetStepsAsMarkdown(Steps);

                var result = Markdig.Markdown.ToHtml(markdown, _markdownPipeline);
                result = "<html><head><meta http-equiv='Content-Type' content='text/html;charset=UTF-8'><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\" />" +
                        "<style>" + _css + "</style></head><body>" + result + "</body></html>";
                return result;

            }
            else
            {
                return "";
            }
        }

        private string GetStepsAsMarkdown(IEnumerable<ActionStep> steps)
        {
            var newLine = "\r\n";

            var sb = new StringBuilder();
            foreach (var s in steps)
            {
                sb.AppendLine(newLine + "# " + s.Title);
                sb.AppendLine(s.Description);

                if (s.Substeps != null)
                {
                    foreach (var sub in s.Substeps)
                    {
                        if (!string.IsNullOrEmpty(sub.Description))
                        {
                            if (sub.Description.Contains("|"))
                            {
                                // table items
                                sb.AppendLine(sub.Description);
                            }
                            else
                            {
                                // list items
                                sb.AppendLine(" - " + sub.Description);
                            }
                        }
                        else
                        {
                            sb.AppendLine(" - " + sub.Title);
                        }
                    }
                }
            }
            return sb.ToString();
        }

        private async void UserControl_IsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                LoadingProgess.Visibility = System.Windows.Visibility.Visible;
                var result = await UpdatePreview();
                MarkdownView.NavigateToString(result);
                LoadingProgess.Visibility = System.Windows.Visibility.Hidden;
            }
        }
    }
}

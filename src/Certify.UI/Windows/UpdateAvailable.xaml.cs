using System.Text;
using System.Threading.Tasks;

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

            this.DataContext = MainViewModel;

            this.Width *= MainViewModel.UIScaleFactor;
            this.Height *= MainViewModel.UIScaleFactor;

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

            _update = update;

        }
        private async Task<string> UpdatePreview()
        {
            // generate release notes

            UpdateMessage.Text = _update.Message.Body;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("**Release Notes**\n");

            sb.AppendLine($"{_update.Message.ReleaseNotesURL}\n");

            if (_update.Message.ReleaseNotes != null)
            {
                foreach (var note in _update.Message.ReleaseNotes)
                {
                    sb.AppendLine($"{note.Version}: {note.ReleaseDate}");
                    sb.AppendLine($"{note.Body}\n");
                }
            }

            var result = Markdig.Markdown.ToHtml(sb.ToString(), _markdownPipeline);
            result = "<html><head><meta http-equiv='Content-Type' content='text/html;charset=UTF-8'><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\" />" +
                    "<style>" + _css + "</style></head><body>" + result + "</body></html>";
            return result;


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

using System;
using System.Windows;
using System.Windows.Input;
using Certify.Locales;

namespace Certify.UI.Windows
{
    /// <summary>
    /// Interaction logic for Feedback.xaml 
    /// </summary>
    public partial class Feedback
    {
        public string FeedbackMessage { get; set; }
        public bool IsException { get; set; }

        protected Certify.UI.ViewModel.AppViewModel MainViewModel => ViewModel.AppViewModel.Current;

        public Feedback(string feedbackMsg, bool isException)
        {
            InitializeComponent();

            if (feedbackMsg != null)
            {
                FeedbackMessage = feedbackMsg;
                Comment.Text = FeedbackMessage;
            }
            IsException = isException;

            if (IsException)
            {
                Prompt.Text = SR.Send_Feedback_Exception;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private async void Submit_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(Comment.Text))
            {
                return;
            }

            Submit.IsEnabled = false;

            //submit feedback if connection available

            var appVersion = Management.Util.GetAppVersion();

            var feedbackReport = new Models.Shared.FeedbackReport
            {
                EmailAddress = EmailAddress.Text,
                Comment = Comment.Text,
                SupportingData = new
                {
                    OS = Environment.OSVersion.ToString(),
                    AppVersion = ConfigResources.AppName + " " + appVersion,
                    IsException = IsException
                },
                AppVersion = ConfigResources.AppName + " " + appVersion,
                IsException = IsException
            };

            if (MainViewModel.PluginManager.DashboardClient != null)
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var submittedOK = await MainViewModel.PluginManager.DashboardClient.SubmitFeedbackAsync(feedbackReport);

                Mouse.OverrideCursor = Cursors.Arrow;

                if (submittedOK)
                {
                    MessageBox.Show(SR.Send_Feedback_Success);
                    Close();
                    return;
                }
                else
                {
                    MessageBox.Show(SR.Send_Feedback_Error);
                }
            }
            else
            {
                //failed
                MessageBox.Show(SR.Send_Feedback_Error);
            }
            Submit.IsEnabled = true;
        }
    }
}

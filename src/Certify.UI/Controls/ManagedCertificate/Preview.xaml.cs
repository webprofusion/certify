using Certify.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Controls;

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

        public Preview()
        {
            InitializeComponent();

            Steps = new ObservableCollection<ActionStep>();
        }

        private async Task UpdatePreview()
        {
            // generate preview
            if (ItemViewModel.SelectedItem != null)
            {
                ItemViewModel.UpdateManagedCertificateSettings();

                List<ActionStep> steps = new List<ActionStep>();
                try
                {
                    steps = await AppViewModel.GetPreviewActions(ItemViewModel.SelectedItem);
                }
                catch (Exception exp)
                {
                    steps.Add(new ActionStep { Title = "Could not generate preview", Description = $"A problem occurred generating the preview: {exp.Message}" });
                }

                Steps = new ObservableCollection<ActionStep>(steps);
                App.Current.Dispatcher.Invoke((Action)delegate
                {
                    this.PreviewSteps.ItemsSource = Steps;
                });
            }
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
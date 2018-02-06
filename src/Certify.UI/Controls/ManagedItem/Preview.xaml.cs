using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Controls;

namespace Certify.UI.Controls.ManagedItem
{
    public class PreviewStep
    {
        public string Title { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Interaction logic for Preview.xaml 
    /// </summary>
    public partial class Preview : UserControl
    {
        protected Certify.UI.ViewModel.AppModel MainViewModel => UI.ViewModel.AppModel.Current;

        private ObservableCollection<PreviewStep> Steps { get; set; }

        public Preview()
        {
            InitializeComponent();

            Steps = new ObservableCollection<PreviewStep>();
        }

        private void UpdatePreview()
        {
            // generate preview
            if (MainViewModel.SelectedItem != null)
            {
                List<PreviewStep> steps = new List<PreviewStep>();
                try
                {
                    var item = MainViewModel.SelectedItem;

                    int stepIndex = 1;

                    // certificate summary
                    string certDescription = "A new certificate will be requested from the Let's Encrypt certificate authority for the following domains:\n";
                    certDescription += $"\n{ item.RequestConfig.PrimaryDomain } (Primary Domain) ";
                    if (item.RequestConfig.SubjectAlternativeNames.Any(s => s != item.RequestConfig.PrimaryDomain))
                    {
                        certDescription += $"\nIncluding the following Subject Alternative Names:\n\n";

                        foreach (var d in item.RequestConfig.SubjectAlternativeNames)
                        {
                            certDescription += $"\t{ d} \n";
                        }
                    }

                    steps.Add(new PreviewStep { Title = "Summary", Description = certDescription });

                    // validation steps
                    string validationDescription = $"Attempt authorization using the {item.RequestConfig.ChallengeType} challenge type.";
                    steps.Add(new PreviewStep { Title = $"{stepIndex}. Domain Validation", Description = validationDescription });
                    stepIndex++;

                    // if using http-01, describe steps

                    // if using dns-01, describe steps

                    // pre request scripting steps
                    if (!String.IsNullOrEmpty(item.RequestConfig.PreRequestPowerShellScript))
                    {
                        steps.Add(new PreviewStep { Title = $"{stepIndex}. Pre-Request Powershell", Description = $"Execute PowerShell Script" });
                        stepIndex++;
                    }

                    // cert request step
                    string certRequest = $"Certificate Signing Request {item.RequestConfig.CSRKeyAlg}.";
                    steps.Add(new PreviewStep { Title = $"{stepIndex}. Certificate Request", Description = certRequest });
                    stepIndex++;

                    // post request scripting steps
                    if (!String.IsNullOrEmpty(item.RequestConfig.PostRequestPowerShellScript))
                    {
                        steps.Add(new PreviewStep { Title = $"{stepIndex}. Post-Request Powershell", Description = $"Execute PowerShell Script" });
                        stepIndex++;
                    }

                    // webhook scripting steps
                    if (!String.IsNullOrEmpty(item.RequestConfig.WebhookUrl))
                    {
                        steps.Add(new PreviewStep { Title = $"{stepIndex}. Post-Request WebHook", Description = $"Execute WebHook {item.RequestConfig.WebhookUrl}" });

                        stepIndex++;
                    }

                    // deployment & binding steps
                    string deploymentDescription = $"IIS Binding creation/update with certificate.";
                    steps.Add(new PreviewStep { Title = $"{stepIndex}. Deployment", Description = deploymentDescription });
                    stepIndex++;
                }
                catch (Exception exp)
                {
                    steps.Add(new PreviewStep { Title = "Could not generate preview", Description = $"A problem occurred generating the preview: {exp.Message}" });
                }

                Steps = new ObservableCollection<PreviewStep>(steps);

                this.PreviewSteps.ItemsSource = Steps;
            }
        }

        private void UserControl_IsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            if (this.IsVisible)
            {
                UpdatePreview();
            }
        }
    }
}
using Certify.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Certify.UI.Controls.ManagedItem
{
    /// <summary>
    /// Interaction logic for Preview.xaml 
    /// </summary>
    public partial class Preview : UserControl
    {
        protected Certify.UI.ViewModel.AppModel MainViewModel => UI.ViewModel.AppModel.Current;

        private ObservableCollection<ActionStep> Steps { get; set; }

        public Preview()
        {
            InitializeComponent();

            Steps = new ObservableCollection<ActionStep>();
        }

        private async Task UpdatePreview()
        {
            // generate preview
            if (MainViewModel.SelectedItem != null)
            {
                List<ActionStep> steps = new List<ActionStep>();
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

                    steps.Add(new ActionStep { Title = "Summary", Description = certDescription });

                    // validation steps
                    string validationDescription = $"Attempt authorization using the {item.RequestConfig.ChallengeType} challenge type.";
                    steps.Add(new ActionStep { Title = $"{stepIndex}. Domain Validation", Description = validationDescription });
                    stepIndex++;

                    // if using http-01, describe steps

                    // if using dns-01, describe steps

                    // pre request scripting steps
                    if (!String.IsNullOrEmpty(item.RequestConfig.PreRequestPowerShellScript))
                    {
                        steps.Add(new ActionStep { Title = $"{stepIndex}. Pre-Request Powershell", Description = $"Execute PowerShell Script" });
                        stepIndex++;
                    }

                    // cert request step
                    string certRequest = $"Certificate Signing Request {item.RequestConfig.CSRKeyAlg}.";
                    steps.Add(new ActionStep { Title = $"{stepIndex}. Certificate Request", Description = certRequest });
                    stepIndex++;

                    // post request scripting steps
                    if (!String.IsNullOrEmpty(item.RequestConfig.PostRequestPowerShellScript))
                    {
                        steps.Add(new ActionStep { Title = $"{stepIndex}. Post-Request Powershell", Description = $"Execute PowerShell Script" });
                        stepIndex++;
                    }

                    // webhook scripting steps
                    if (!String.IsNullOrEmpty(item.RequestConfig.WebhookUrl))
                    {
                        steps.Add(new ActionStep { Title = $"{stepIndex}. Post-Request WebHook", Description = $"Execute WebHook {item.RequestConfig.WebhookUrl}" });

                        stepIndex++;
                    }

                    // deployment & binding steps
                    string deploymentDescription = $"IIS Binding creation/update with certificate.";
                    var deploymentStep = new ActionStep { Title = $"{stepIndex}. Deployment", Description = deploymentDescription };

                    // add deployment sub-steps (if any)
                    var bindingRequest = await MainViewModel.ReapplyCertificateBindings(item.Id, true);
                    if (bindingRequest.Actions != null) deploymentStep.Substeps = bindingRequest.Actions;

                    steps.Add(deploymentStep);
                    stepIndex++;

                    stepIndex = steps.Count;
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
using System;
using System.Linq;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;

namespace Certify.CLI
{
    public partial class CertifyCLI
    {
        public async Task<bool> PerformDeployment(string managedCertName, string taskName)
        {

            var managedCertificates = await _certifyClient.GetManagedCertificates(new ManagedCertificateFilter { Name = managedCertName });

            if (managedCertificates.Count == 1)
            {
                var managedCert = managedCertificates.Single();

                if (!string.IsNullOrEmpty(taskName))
                {
                    // identify specific task
                    Console.WriteLine("Performing deployment task '" + taskName + "'..");
                    var task = managedCert.PostRequestTasks.FirstOrDefault(t => t.TaskName.ToLowerInvariant().Trim() == taskName.ToLowerInvariant().Trim());

                    if (task != null)
                    {
                        var results = await _certifyClient.PerformDeployment(managedCert.Id, task.Id, isPreviewOnly: false);

                        if (results.Any(r => r.HasError == true))
                        {
                            var err = results.First(f => f.HasError == true);
                            Console.WriteLine("One or more task steps failed: " + err.Description);
                            return false;
                        }
                        else
                        {
                            Console.WriteLine("Deployment task completed.");
                            return true;
                        }
                    }
                    else
                    {
                        Console.WriteLine("Task '" + taskName + "' not found. Deployment failed.");
                        return false;
                    }
                }
                else
                {
                    // perform all deployment tasks
                    Console.WriteLine("Performing all deployment tasks for managed certificate '" + managedCertName + "'..");

                    var results = await _certifyClient.PerformDeployment(managedCert.Id, null, isPreviewOnly: false);

                    if (results.Any(r => r.HasError == true))
                    {
                        var err = results.First(f => f.HasError == true);
                        Console.WriteLine("One or more task steps failed: " + err.Description);
                        return false;
                    }
                    else
                    {
                        Console.WriteLine("Deployment tasks completed.");
                        return true;
                    }
                }
            }
            else
            {
                if (managedCertificates.Count > 1)
                {
                    // too many matches
                    Console.WriteLine("Managed Certificate name matched more than one item. Deployment failed.");
                    return false;
                }
                else
                {
                    // no matches
                    Console.WriteLine("Managed Certificate name has no matches. Deployment failed.");
                    return false;
                }
            }
     
        }
    }
}

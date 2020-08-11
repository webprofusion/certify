using System;
using System.Linq;
using System.Threading.Tasks;
using Certify.Management;
using Certify.Models;

namespace Certify.CLI
{
    public partial class CertifyCLI
    {
        public async Task<bool> PerformDeployment(string managedCertId, string taskId)
        {

            var managedCert = await _certifyClient.GetManagedCertificate(managedCertId);

            if (managedCert != null)
            {
                if (!string.IsNullOrEmpty(taskId))
                {
                    // identify specific task

                    var task = managedCert.PostRequestTasks.FirstOrDefault(t => t.Id.ToLowerInvariant().Trim() == taskId.ToLowerInvariant().Trim());

                    Console.WriteLine($"Performing deployment task [{task.TaskName}] for managed certificate [{managedCert.Name}]..");

                    if (task != null)
                    {
                        var results = await _certifyClient.PerformDeployment(managedCert.Id, task.Id, isPreviewOnly: false, forceTaskExecute: false);

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
                        Console.WriteLine("Task '" + taskId + "' not found. Deployment failed.");
                        return false;
                    }
                }
                else
                {
                    // perform all deployment tasks
                    Console.WriteLine($"Performing all deployment tasks for managed certificate [{managedCert.Name}]..");

                    var results = await _certifyClient.PerformDeployment(managedCert.Id, null, isPreviewOnly: false, forceTaskExecute: false);

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
                // no matches
                Console.WriteLine("Managed Certificate Id has no matches. Deployment failed.");
                return false;
            }

        }
    }
}

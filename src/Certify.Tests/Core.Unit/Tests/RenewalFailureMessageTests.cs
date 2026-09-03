using System.Reflection;
using Certify.Config;
using Certify.Management;
using Certify.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    /// <summary>
    /// Tests for the message a failed renewal reports. It is what the operator sees against the item and what status
    /// reports carry, so it has to name the stage which actually failed rather than the last thing the request said.
    /// A renewal which obtained a certificate but failed to deploy it reads as a success unless the deployment stage
    /// gets to speak
    /// </summary>
    [TestClass]
    public class RenewalFailureMessageTests
    {
        private static string InvokeGetDeploymentTaskFailureMessage(ManagedCertificate managedCertificate)
        {
            var method = typeof(CertifyManager).GetMethod("GetDeploymentTaskFailureMessage", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "GetDeploymentTaskFailureMessage should be available for testing");

            return (string)method.Invoke(null, new object[] { managedCertificate });
        }

        private static string InvokeResolveOverallRenewalMessage(ManagedCertificate managedCertificate, CertificateRequestResult result, RequestState finalState, bool postRequestTasksRan)
        {
            var method = typeof(CertifyManager).GetMethod("ResolveOverallRenewalMessage", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "ResolveOverallRenewalMessage should be available for testing");

            return (string)method.Invoke(null, new object[] { managedCertificate, result, finalState, postRequestTasksRan });
        }

        [TestMethod, Description("An item with nothing failing has no deployment task failure to report")]
        public void NoFailedTasksReportsNothing()
        {
            var item = new ManagedCertificate
            {
                PostRequestTasks =
                [
                    new DeploymentTaskConfig { TaskName = "Upload", LastRunStatus = RequestState.Success }
                ]
            };

            Assert.AreEqual(string.Empty, InvokeGetDeploymentTaskFailureMessage(item));
        }

        [TestMethod, Description("A single failed task reports the reason it gave")]
        public void SingleFailedTaskReportsItsOwnResult()
        {
            var item = new ManagedCertificate
            {
                PostRequestTasks =
                [
                    new DeploymentTaskConfig { TaskName = "Upload", LastRunStatus = RequestState.Error, LastResult = "SFTP host unreachable" }
                ]
            };

            Assert.AreEqual("SFTP host unreachable", InvokeGetDeploymentTaskFailureMessage(item),
                "The task's own reason is more use to the operator than the fact that a task failed");
        }

        [TestMethod, Description("A failed task which gave no reason is reported by name")]
        public void SingleFailedTaskWithoutAResultIsNamed()
        {
            var item = new ManagedCertificate
            {
                PostRequestTasks =
                [
                    new DeploymentTaskConfig { TaskName = "Upload", LastRunStatus = RequestState.Error, LastResult = null }
                ]
            };

            Assert.AreEqual("Deployment Task failed: Upload", InvokeGetDeploymentTaskFailureMessage(item));
        }

        [TestMethod, Description("Several failed tasks are all named, rather than only the first")]
        public void MultipleFailedTasksAreAllNamed()
        {
            var item = new ManagedCertificate
            {
                PostRequestTasks =
                [
                    new DeploymentTaskConfig { TaskName = "Upload", LastRunStatus = RequestState.Error, LastResult = "SFTP host unreachable" },
                    new DeploymentTaskConfig { TaskName = "Restart", LastRunStatus = RequestState.Success },
                    new DeploymentTaskConfig { TaskName = "Notify", LastRunStatus = RequestState.Error, LastResult = "Webhook returned 500" }
                ]
            };

            // reporting one task's reason would leave the operator fixing that one and finding the renewal still failing
            Assert.AreEqual("Deployment Tasks failed: Upload, Notify", InvokeGetDeploymentTaskFailureMessage(item));
        }

        [TestMethod, Description("A failed manual task is not reported against an automated renewal")]
        public void FailedManualTaskIsNotReported()
        {
            var item = new ManagedCertificate
            {
                PostRequestTasks =
                [
                    new DeploymentTaskConfig { TaskName = "Ad-hoc export", TaskTrigger = TaskTriggerType.MANUAL, LastRunStatus = RequestState.Error, LastResult = "Export path not writable" }
                ]
            };

            // a manual task is only ever run on demand by a person, so its result is not part of the outcome of an
            // automated request - reporting it would leave the item failed until someone re-ran the task by hand
            Assert.AreEqual(string.Empty, InvokeGetDeploymentTaskFailureMessage(item));
        }

        [TestMethod, Description("A renewal which failed at the deployment task stage reports the task, not the certificate request")]
        public void FailedRenewalReportsTheFailingDeploymentTask()
        {
            var item = new ManagedCertificate
            {
                LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Success, Message = "Certificate issued." },
                PostRequestTasks =
                [
                    new DeploymentTaskConfig { TaskName = "Notify", LastRunStatus = RequestState.Error, LastResult = "Webhook returned 500" }
                ]
            };

            var result = new CertificateRequestResult(item, isSuccess: true, "Certificate issued.")
            {
                PrimaryRequest = new RequestStageStatus { Status = RequestState.Success, Message = "Certificate issued." }
            };

            Assert.AreEqual("Webhook returned 500",
                InvokeResolveOverallRenewalMessage(item, result, RequestState.Error, postRequestTasksRan: true),
                "The request succeeded, so reporting its message would describe the renewal as having gone fine");
        }

        [TestMethod, Description("A renewal which failed to deploy the certificate reports the deployment, not the certificate request")]
        public void FailedRenewalReportsTheBindingDeploymentFailure()
        {
            var item = new ManagedCertificate
            {
                LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Success, Message = "Certificate issued." },
                LastBindingDeployment = new RequestStageStatus { Status = RequestState.Error, Message = "Could not update IIS binding for www.example.com" }
            };

            var result = new CertificateRequestResult(item, isSuccess: true, "Certificate issued.")
            {
                PrimaryRequest = new RequestStageStatus { Status = RequestState.Success, Message = "Certificate issued." }
            };

            Assert.AreEqual("Could not update IIS binding for www.example.com",
                InvokeResolveOverallRenewalMessage(item, result, RequestState.Error, postRequestTasksRan: false));
        }

        [TestMethod, Description("A deployment failure is reported ahead of a deployment task failure")]
        public void DeploymentFailureIsReportedAheadOfATaskFailure()
        {
            var item = new ManagedCertificate
            {
                LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Success, Message = "Certificate issued." },
                LastBindingDeployment = new RequestStageStatus { Status = RequestState.Error, Message = "Could not update IIS binding for www.example.com" },
                PostRequestTasks =
                [
                    new DeploymentTaskConfig { TaskName = "Notify", LastRunStatus = RequestState.Error, LastResult = "Webhook returned 500" }
                ]
            };

            var result = new CertificateRequestResult(item, isSuccess: true, "Certificate issued.")
            {
                PrimaryRequest = new RequestStageStatus { Status = RequestState.Success, Message = "Certificate issued." }
            };

            // the certificate never reached the target, which is the thing to fix first: the task failure most likely
            // follows from it
            Assert.AreEqual("Could not update IIS binding for www.example.com",
                InvokeResolveOverallRenewalMessage(item, result, RequestState.Error, postRequestTasksRan: true));
        }

        [TestMethod, Description("A failed renewal which recorded no stage message still reports something")]
        public void FailedRenewalWithNoStageMessageFallsBackToTheRecordedFailure()
        {
            var item = new ManagedCertificate
            {
                LastPrimaryRequest = new RequestStageStatus { Status = RequestState.Error, Message = null },
                RenewalFailureMessage = "The last thing recorded about this item"
            };

            var result = new CertificateRequestResult(item, isSuccess: false, null)
            {
                PrimaryRequest = new RequestStageStatus { Status = RequestState.Error, Message = null }
            };

            Assert.AreEqual("The last thing recorded about this item",
                InvokeResolveOverallRenewalMessage(item, result, RequestState.Error, postRequestTasksRan: false),
                "An operator looking at a failed item must not be shown a blank reason");
        }

        [TestMethod, Description("A renewal which succeeded without saying anything still reports success")]
        public void SuccessfulRenewalWithNoMessageReportsCompletion()
        {
            var item = new ManagedCertificate();
            var result = new CertificateRequestResult(item, isSuccess: true, null)
            {
                PrimaryRequest = new RequestStageStatus { Status = RequestState.Success }
            };

            Assert.AreEqual("Renewal completed OK.",
                InvokeResolveOverallRenewalMessage(item, result, RequestState.Success, postRequestTasksRan: true));
        }
    }
}

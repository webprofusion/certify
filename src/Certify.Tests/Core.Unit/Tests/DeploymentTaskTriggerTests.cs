using System.Reflection;
using Certify.Config;
using Certify.Management;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Certify.Tests.Core.Unit.Tests
{
    [TestClass]
    public class DeploymentTaskTriggerTests
    {
        private static bool InvokeShouldContinueAfterPreviousTaskFailure(TaskTriggerType taskTrigger, bool primaryRequestSucceeded)
        {
            var method = typeof(CertifyManager).GetMethod("ShouldContinueAfterPreviousTaskFailure", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method);

            return (bool)method.Invoke(null, new object[] { taskTrigger, primaryRequestSucceeded });
        }

        private static bool InvokeShouldSkipTaskBecausePreviousTaskFailed(bool previousTaskFailed, bool runIfLastStepFailed, TaskTriggerType taskTrigger, bool primaryRequestSucceeded)
        {
            var method = typeof(CertifyManager).GetMethod("ShouldSkipTaskBecausePreviousTaskFailed", BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(method);

            return (bool)method.Invoke(null, new object[] { previousTaskFailed, runIfLastStepFailed, taskTrigger, primaryRequestSucceeded });
        }

        [DataTestMethod]
        [DataRow(TaskTriggerType.ANY_STATUS, false, true)]
        [DataRow(TaskTriggerType.ON_ERROR, false, true)]
        [DataRow(TaskTriggerType.ON_TASK_ERROR, false, true)]
        [DataRow(TaskTriggerType.ON_SUCCESS, false, false)]
        [DataRow(TaskTriggerType.ANY_STATUS, true, false)]
        [DataRow(TaskTriggerType.ON_TASK_ERROR, true, true)]
        public void ShouldContinueAfterPreviousTaskFailure_ReturnsExpectedResult(TaskTriggerType taskTrigger, bool primaryRequestSucceeded, bool expected)
        {
            var result = InvokeShouldContinueAfterPreviousTaskFailure(taskTrigger, primaryRequestSucceeded);

            Assert.AreEqual(expected, result);
        }

        [DataTestMethod]
        [DataRow(true, true, TaskTriggerType.ON_SUCCESS, true, false)]
        [DataRow(true, false, TaskTriggerType.ON_SUCCESS, true, true)]
        [DataRow(true, false, TaskTriggerType.ANY_STATUS, false, false)]
        [DataRow(false, false, TaskTriggerType.ON_SUCCESS, true, false)]
        public void ShouldSkipTaskBecausePreviousTaskFailed_HonorsRunIfLastStepFailed(bool previousTaskFailed, bool runIfLastStepFailed, TaskTriggerType taskTrigger, bool primaryRequestSucceeded, bool expected)
        {
            var result = InvokeShouldSkipTaskBecausePreviousTaskFailed(previousTaskFailed, runIfLastStepFailed, taskTrigger, primaryRequestSucceeded);

            Assert.AreEqual(expected, result);
        }
    }
}

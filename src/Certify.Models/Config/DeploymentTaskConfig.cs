using System;
using System.Collections.Generic;
using System.Text;
using Certify.Models.Config;

namespace Certify.Config
{
    public class DeploymentTaskConfig
    {
        /// <summary>
        /// id of task provider to instantiate
        /// </summary>
        public string TaskType { get; set; }

        /// <summary>
        /// Unique task name (id) used in logs and to invoke this deployment task manually
        /// </summary>
        public string TaskName { get; set; }
        /// <summary>
        /// Optional list of stored credential ids required to complete deployment task
        /// </summary>
        public Dictionary<string, string> Credentials { get; set; }

        /// <summary>
        /// If true, deployment task execution is deferred until invoked by command line/scheduled task or manually run
        /// </summary>
        public bool IsDeferred { get; set; }

        /// <summary>
        /// if true, deployment will stop at this step and report as an error, deployment is not considered complete
        /// if false, error will be logged as a warning, next deployment step will continue and overall deployment will be marked as successful (depending on other deployment steps)
        /// </summary>
        public bool IsFatalOnError { get; set; }
    }
}

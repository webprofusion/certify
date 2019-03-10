using System;
using System.Collections.Generic;
using System.Text;
using Certify.Models.Config;

namespace Certify.Config
{
    public class DeploymentTaskConfig
    {

        public string Id { get; set; }
        /// <summary>
        /// id of task provider to instantiate
        /// </summary>
        public string TaskTypeId { get; set; }

        /// <summary>
        /// Unique task name (id) used in logs and to invoke this deployment task manually
        /// </summary>
        public string TaskName { get; set; }
    
        /// <summary>
        /// Optional description for this deployment tasks (i.e. what it does and why)
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// If true, deployment task execution is deferred until invoked by command line/scheduled task or manually run
        /// </summary>
        public bool IsDeferred { get; set; }

        /// <summary>
        /// if true, deployment will stop at this step and report as an error, deployment is not considered complete
        /// if false, error will be logged as a warning, next deployment step will continue and overall deployment will be marked as successful (depending on other deployment steps)
        /// </summary>
        public bool IsFatalOnError { get; set; }

        /// <summary>
        /// If greater than 0, attempt up to N retries before failing
        /// </summary>
        public int RetriesAllowed { get; set; }

        /// <summary>
        /// Time to wait between retry attempts
        /// </summary>
        public int RetryDelaySeconds { get; set; } = 10;

        public string ChallengeProvider { get; set; }
        public string ChallengeCredentialKey { get; set; }

        /// <summary>
        /// hostname or IP of target (if required)
        /// </summary>
        public string TargetHost { get; set; }

        /// <summary>
        /// Local, Windows (Network), SSH etc
        /// </summary>
        //public string TargetType { get; set; }

        // Dictionary of provider parameter values
        public List<ProviderParameterSetting> Parameters { get; set; }
    }
}

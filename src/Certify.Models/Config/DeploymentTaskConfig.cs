using System;
using System.Collections.Generic;
using Certify.Models;
using Certify.Models.Config;

namespace Certify.Config
{

    public enum TaskTriggerType
    {
        /// <summary>
        /// Task will not run
        /// </summary>
        NOT_ENABLED = 0,
        /// <summary>
        /// Task will run for any status
        /// </summary>
        ANY_STATUS = 1,
        /// <summary>
        /// Task will run if the primary request succeeded
        /// </summary>
        ON_SUCCESS = 2,
        /// <summary>
        /// Task will run if the primary request failed
        /// </summary>
        ON_ERROR = 4,
        /// <summary>
        /// Manual tasks don't run automatically and are only started by the user via the UI or via the command line
        /// </summary>
        MANUAL = 8
    }

    public class DeploymentTaskTypes
    {
        public static Dictionary<string, string> TargetTypes { get; set; } = new Dictionary<string, string>
        {
            { StandardAuthTypes.STANDARD_AUTH_LOCAL,"Local (as current service user)"},
            { StandardAuthTypes.STANDARD_AUTH_LOCAL_AS_USER,"Local (as specific user)"},
            { StandardAuthTypes.STANDARD_AUTH_WINDOWS,"Windows (Network)"},
            { StandardAuthTypes.STANDARD_AUTH_SSH,"SSH (Remote)"}
        };

        public static Dictionary<TaskTriggerType, string> TriggerTypes { get; set; } = new Dictionary<TaskTriggerType, string>
        {
            { TaskTriggerType.NOT_ENABLED,"Disabled (Will Not Run)"},
            { TaskTriggerType.ANY_STATUS,"Run On Success or On Error"},
            { TaskTriggerType.ON_SUCCESS,"Run On Success"},
            { TaskTriggerType.ON_ERROR,"Run On Error"},
            { TaskTriggerType.MANUAL,"Manual (run using UI or command line)"}
        };
    }

    public class DeploymentTaskConfig
    {

        public string? Id { get; set; }
        /// <summary>
        /// id of task provider to instantiate
        /// </summary>
        public string? TaskTypeId { get; set; }

        /// <summary>
        /// Unique task name (id) used in logs and to invoke this deployment task manually
        /// </summary>
        public string? TaskName { get; set; } = string.Empty;

        /// <summary>
        /// Optional description for this deployment tasks (i.e. what it does and why)
        /// </summary>
        public string? Description { get; set; } = string.Empty;

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

        /// <summary>
        /// The challenge provider is the authentication type required (Local, Network, SSH etc)
        /// </summary>
        public string? ChallengeProvider { get; set; } = string.Empty;
        public string? ChallengeCredentialKey { get; set; } = string.Empty;

        /// <summary>
        /// hostname or IP of target (if required)
        /// </summary>
        public string? TargetHost { get; set; } = string.Empty;

        /// <summary>
        ///Dictionary of provider parameter values
        /// </summary>
        public List<ProviderParameterSetting>? Parameters { get; set; } = new();

        public DateTime? DateLastExecuted { get; set; }
        public string? LastResult { get; set; }
        public RequestState? LastRunStatus { get; set; }

        /// <summary>
        /// The request result state which triggers the task (All, Success, Error) 
        /// </summary>
        public TaskTriggerType TaskTrigger { get; set; } = TaskTriggerType.ANY_STATUS;

        /// <summary>
        /// If true, this task will run even if the last task in the sequence failed (default=false)
        /// </summary>
        public bool RunIfLastStepFailed { get; set; }
    }
}

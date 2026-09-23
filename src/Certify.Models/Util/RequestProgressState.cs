using System;
using System.Collections.Generic;

namespace Certify.Models
{
    public enum RequestState
    {
        /// <summary>
        /// Request is not running 
        /// </summary>
        NotRunning = 0,

        /// <summary>
        /// Request is queued for renewal attempt
        /// </summary>
        Queued = 6,

        /// <summary>
        /// Request is in progress 
        /// </summary>
        Running = 1,

        /// <summary>
        /// Request has failed 
        /// </summary>
        Error = 2,

        /// <summary>
        /// Request has succeeded 
        /// </summary>
        Success = 3,

        /// <summary>
        /// Request is waiting on user input 
        /// </summary>
        Paused = 4,

        /// <summary>
        /// Request has been skipped due to temporary condition 
        /// </summary>
        Warning = 5,

        /// <summary>
        /// Request has been intentionally skipped due to a configuration or logical condition
        /// </summary>
        Skipped = 7
    }

    /// <summary>
    /// The fixed stages every certificate request run passes through, in order. A run may skip stages which do not
    /// apply to it (e.g. an item with no pre-request tasks, or a redeploy which only has a deployment stage), but the
    /// order is always the same so a run can be shown against the full set.
    /// </summary>
    public enum RequestStage
    {
        /// <summary>
        /// Waiting to start, e.g. as part of a scheduled renewal pass
        /// </summary>
        Queued = 0,

        /// <summary>
        /// Running the tasks configured to run before the certificate request
        /// </summary>
        PreRequestTasks = 1,

        /// <summary>
        /// Creating (or resuming) the certificate order with the certificate authority
        /// </summary>
        Order = 2,

        /// <summary>
        /// Preparing the challenge responses for each identifier (http resources, DNS records etc)
        /// </summary>
        Challenges = 3,

        /// <summary>
        /// Waiting for challenge responses (usually DNS records) to propagate before validation
        /// </summary>
        Propagation = 4,

        /// <summary>
        /// The certificate authority validating each challenge response
        /// </summary>
        Validation = 5,

        /// <summary>
        /// Finalising the order and obtaining the certificate (or fetching it from a subscription source)
        /// </summary>
        Certificate = 6,

        /// <summary>
        /// Storing and binding the certificate and running the deployment tasks
        /// </summary>
        Deployment = 7
    }

    public static class RequestStageInfo
    {
        /// <summary>
        /// All stages in the order a run passes through them
        /// </summary>
        public static readonly RequestStage[] All =
        {
            RequestStage.Queued,
            RequestStage.PreRequestTasks,
            RequestStage.Order,
            RequestStage.Challenges,
            RequestStage.Propagation,
            RequestStage.Validation,
            RequestStage.Certificate,
            RequestStage.Deployment
        };

        /// <summary>
        /// Name of the stage as shown to people
        /// </summary>
        public static string GetDisplayName(RequestStage? stage)
        {
            switch (stage)
            {
                case RequestStage.Queued: return "Queued";
                case RequestStage.PreRequestTasks: return "Pre-request tasks";
                case RequestStage.Order: return "Order";
                case RequestStage.Challenges: return "Challenges";
                case RequestStage.Propagation: return "Propagation";
                case RequestStage.Validation: return "Validation";
                case RequestStage.Certificate: return "Certificate";
                case RequestStage.Deployment: return "Deployment";
                default: return "Request";
            }
        }
    }

    /// <summary>
    /// What started a certificate request run
    /// </summary>
    public enum RequestTrigger
    {
        Unknown = 0,

        /// <summary>
        /// The scheduled renewal pass
        /// </summary>
        Schedule = 1,

        /// <summary>
        /// A person (or API client) asked for this item's request
        /// </summary>
        User = 2,

        /// <summary>
        /// A renewal pass requested explicitly (e.g. Renew All), rather than the scheduled pass
        /// </summary>
        RenewAll = 3
    }

    /// <summary>
    /// Something a person must do before a paused request can continue, such as creating a DNS record for a manual
    /// DNS challenge
    /// </summary>
    public class RequestUserAction
    {
        /// <summary>
        /// Identifier (domain etc) the action relates to
        /// </summary>
        public string? Identifier { get; set; }

        /// <summary>
        /// Short description of the action
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// For a DNS action, the full name of the record to create
        /// </summary>
        public string? RecordName { get; set; }

        /// <summary>
        /// For a DNS action, the record type (TXT)
        /// </summary>
        public string? RecordType { get; set; }

        /// <summary>
        /// For a DNS action, the value the record must hold
        /// </summary>
        public string? RecordValue { get; set; }

        /// <summary>
        /// Full instructions, as shown to the user
        /// </summary>
        public string? Instructions { get; set; }
    }

    public class RequestProgressManagedItem
    {
        public string Id { get; set; } = string.Empty;

        // This is the instance id of the managed certificate in the hub
        public string InstanceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FailureMessage { get; set; } = string.Empty;
        public int FailureCount { get; set; }
    }

    public class RequestProgressState : BindableBase
    {
        public bool IsPreviewMode { get; set; }
        public bool IsSkipped { get; set; }
        public RequestProgressManagedItem? ManagedCertificate { get; set; }

        public RequestProgressState(RequestState currentState, string msg, ManagedCertificate item, bool isPreviewMode = false, bool isSkipped = false)
        {
            CurrentState = currentState;
            Message = msg;
            ManagedCertificate = new RequestProgressManagedItem
            {
                Id = item.Id ?? "",
                InstanceId = item.InstanceId ?? "",
                Name = item.Name ?? "",
                FailureMessage = item.RenewalFailureMessage ?? "",
                FailureCount = item.RenewalFailureCount
            };

            IsPreviewMode = isPreviewMode;
            IsSkipped = isSkipped;
            MessageCreated = DateTimeOffset.UtcNow;
        }

        public RequestProgressState()
        {
            CurrentState = RequestState.NotRunning;
            MessageCreated = DateTimeOffset.UtcNow;
        }

        public bool IsRunning => CurrentState == RequestState.Running ? true : false;

        public RequestState CurrentState { get; set; }

        public string? Message { get; set; }

        public DateTimeOffset MessageCreated { get; set; }
        public object? Result { get; set; }

        /// <summary>
        /// Identifies the request run this message belongs to. Each attempt at a request is a new run, so two
        /// attempts for the same item can be told apart. Null for messages from older instances, or which are not
        /// part of a request run (e.g. a configuration test).
        /// </summary>
        public string? RunId { get; set; }

        /// <summary>
        /// The stage of the request run this message was reported in
        /// </summary>
        public RequestStage? Stage { get; set; }

        /// <summary>
        /// What started the request run
        /// </summary>
        public RequestTrigger Trigger { get; set; }

        /// <summary>
        /// Why the request run was started, e.g. the renewal reason from the renewal schedule
        /// </summary>
        public string? TriggerReason { get; set; }

        /// <summary>
        /// Identifies the renewal pass the run belongs to, if any, so the runs of one pass can be grouped
        /// </summary>
        public string? BatchId { get; set; }

        /// <summary>
        /// When the run was queued or started
        /// </summary>
        public DateTimeOffset? RunStarted { get; set; }

        /// <summary>
        /// When waiting (e.g. for DNS propagation), the time the wait ends. A client shows a countdown to this time
        /// rather than the service reporting each second of the wait.
        /// </summary>
        public DateTimeOffset? WaitUntil { get; set; }

        /// <summary>
        /// True for the last message of a request run, carrying its outcome. Intermediate messages can report a
        /// success (e.g. the certificate was issued) while the run continues, so only this marks the run finished.
        /// </summary>
        public bool IsFinal { get; set; }

        /// <summary>
        /// The stages the run has passed through so far, with their timings, so a client which joins part way through
        /// a run can show its progress without having seen every earlier message
        /// </summary>
        public List<RequestStageStatus>? Stages { get; set; }

        /// <summary>
        /// Actions a person must take before a paused run can continue
        /// </summary>
        public List<RequestUserAction>? UserActions { get; set; }

        public string Id
        {
            get
            {
                if (ManagedCertificate != null)
                {
                    return ManagedCertificate?.Id ?? string.Empty;
                }
                else
                {
                    return string.Empty;
                }
            }
        }

        public void ProgressReport(RequestProgressState state)
        {
            CurrentState = state.CurrentState;
            Message = state.Message;
            Result = state.Result;
            RunId = state.RunId;
            Stage = state.Stage;
            Trigger = state.Trigger;
            TriggerReason = state.TriggerReason;
            BatchId = state.BatchId;
            RunStarted = state.RunStarted;
            WaitUntil = state.WaitUntil;
            IsFinal = state.IsFinal;
            Stages = state.Stages;
            UserActions = state.UserActions;

#if DEBUG
            System.Diagnostics.Debug.WriteLine(ManagedCertificate?.Name + ": " + CurrentState.ToString() + (Message != null ? ", " + Message : ""));
#endif
        }
    }
}

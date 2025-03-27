using System;

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

    public class RequestProgressManagedItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string FailureMessage { get; set; } = string.Empty;
        public int FailureCount { get; set; }
        public RequestProgressManagedItem(string? id, string? name, string? failureMessage, int failureCount)
        {
            Id = id ?? "";
            Name = name ?? "";
            FailureMessage = failureMessage ?? "";
            FailureCount = failureCount;
        }
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
            ManagedCertificate = new RequestProgressManagedItem(item.Id, item.Name, item.RenewalFailureMessage, item.RenewalFailureCount);
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

#if DEBUG
            System.Diagnostics.Debug.WriteLine(ManagedCertificate?.Name + ": " + CurrentState.ToString() + (Message != null ? ", " + Message : ""));
#endif
        }
    }
}

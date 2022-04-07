namespace Certify.Models
{
    public enum RequestState
    {
        /// <summary>
        /// Request is not running 
        /// </summary>
        NotRunning = 0,

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
        Paused = 4
    }

    public class RequestProgressState : BindableBase
    {
        public bool IsPreviewMode { get; set; }
        public ManagedCertificate? ManagedCertificate { get; set; }

        public RequestProgressState(RequestState currentState, string msg, ManagedCertificate item, bool isPreviewMode = false)
        {
            CurrentState = currentState;
            Message = msg;
            ManagedCertificate = item;
            IsPreviewMode = isPreviewMode;
        }

        public RequestProgressState()
        {
            CurrentState = RequestState.NotRunning;

        }

        public bool IsRunning => CurrentState == RequestState.Running ? true : false;

        public RequestState CurrentState { get; set; }

        public string? Message { get; set; }

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

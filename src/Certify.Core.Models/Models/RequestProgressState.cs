using System;
using System.Linq;

namespace Certify.Models
{
    public enum RequestState
    {
        NotRunning,
        Running,
        Error,
        Success
    }

    public class RequestProgressState : BindableBase
    {
        public ManagedItem ManagedItem { get; set; }

        public RequestProgressState(RequestState currentState, string msg, ManagedItem item)
        {
            CurrentState = currentState;
            Message = msg;
            ManagedItem = item;
        }

        public bool IsRunning
        {
            get
            {
                return CurrentState == RequestState.Running ? true : false;
            }
        }

        public RequestState CurrentState { get; set; }

        public string Message { get; set; }

        public object Result { get; set; }

        public string Id
        {
            get
            {
                if (ManagedItem != null) return ManagedItem.Id;
                return null;
            }
        }

        public void ProgressReport(RequestProgressState state)
        {
            this.CurrentState = state.CurrentState;
            this.Message = state.Message;
            this.Result = state.Result;

#if DEBUG
            System.Diagnostics.Debug.WriteLine(ManagedItem.Name + ": " + CurrentState.ToString() + (Message != null ? ", " + Message : ""));
#endif
        }
    }
}
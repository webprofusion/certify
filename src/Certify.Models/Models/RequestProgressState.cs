using System;
using System.Linq;

namespace Certify.Models
{
    public enum RequestState
    {
        NotRunning=0,
        Running=1,
        Error=2,
        Success=3
    }

    public class RequestProgressState : BindableBase
    {
        public ManagedCertificate ManagedCertificate { get; set; }

        public RequestProgressState(RequestState currentState, string msg, ManagedCertificate item)
        {
            CurrentState = currentState;
            Message = msg;
            ManagedCertificate = item;
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
                if (ManagedCertificate != null) return ManagedCertificate.Id;
                return null;
            }
        }

        public void ProgressReport(RequestProgressState state)
        {
            this.CurrentState = state.CurrentState;
            this.Message = state.Message;
            this.Result = state.Result;

#if DEBUG
            System.Diagnostics.Debug.WriteLine(ManagedCertificate.Name + ": " + CurrentState.ToString() + (Message != null ? ", " + Message : ""));
#endif
        }
    }
}
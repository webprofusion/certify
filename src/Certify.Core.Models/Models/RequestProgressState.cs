using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public bool IsRunning { get; set; }

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

            if (CurrentState != RequestState.Running)
            {
                if (IsRunning != false) IsRunning = false;
            }
            else
            {
                if (IsRunning != true) IsRunning = true;
            }

#if DEBUG
            System.Diagnostics.Debug.WriteLine(ManagedItem.Name + ": " + CurrentState.ToString() + (Message != null ? ", " + Message : ""));
#endif
        }
    }
}
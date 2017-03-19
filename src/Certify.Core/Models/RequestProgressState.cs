using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Models
{
    public enum RequestState
    {
        InProgress,
        Error,
        Success
    }

    public class RequestProgressState : BindableBase
    {
        public ManagedItem ManagedItem { get; set; }
        public bool IsRunning;

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
            this.IsRunning = state.IsRunning;
            this.CurrentState = state.CurrentState;
            this.Message = state.Message;
            this.Result = state.Result;

            if (CurrentState != RequestState.InProgress)
            {
                IsRunning = false;
            }
            else
            {
                IsRunning = true;
            }

#if DEBUG
            System.Diagnostics.Debug.WriteLine(ManagedItem.Name + ": " + CurrentState.ToString() + (Message != null ? ", " + Message : ""));
#endif
        }
    }
}
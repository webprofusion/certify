using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.UI.Shared
{
    public enum NotificationType
    {
        Info = 1,
        Success = 2,
        Error = 3,
        Warning = 4
    }
    public enum PrimaryUITabs
    {
        ManagedCertificates = 0,
        CurrentProgress = 1
    }

    public interface ICertifyApp
    {
        string ToggleTheme(string initialTheme = null);
        void ShowNotification(string msg, NotificationType type = NotificationType.Info, bool autoClose = true);
    }
}

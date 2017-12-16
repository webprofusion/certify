using Certify.Locales;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Certify.UI.Utils
{
    public class UpdateCheckUtils
    {
        public async Task<Models.UpdateCheck> UpdateWithDownload()
        {
            Mouse.OverrideCursor = Cursors.Wait;
            var updateCheck = await new Management.Util().DownloadUpdate();
            if (!string.IsNullOrEmpty(updateCheck.UpdateFilePath))
            {
                if (MessageBox.Show(SR.Update_ReadyToApply, ConfigResources.AppName, MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes)
                {
                    // file has been downloaded and verified
                    System.Diagnostics.Process p = new System.Diagnostics.Process();
                    p.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                    p.StartInfo.FileName = updateCheck.UpdateFilePath;
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.RedirectStandardError = true;
                    p.StartInfo.Arguments = "/SILENT";
                    p.Start();

                    //stop certify.service
                    /*ServiceController service = new ServiceController("ServiceName");
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped);*/

                    Application.Current.Shutdown();
                }
            }
            Mouse.OverrideCursor = Cursors.Arrow;
            return updateCheck;
        }
    }
}
using Certify.Locales;
using System;
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
            Models.UpdateCheck updateCheck;

            try
            {
                updateCheck = await new Management.Util().DownloadUpdate();
            }
            catch (Exception exp)
            {
                //could not complete or verify download
                MessageBox.Show("Sorry, the update could not be downloaded. Please try again later.");
                System.Diagnostics.Debug.WriteLine(exp.ToString());
                return null;
            }

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
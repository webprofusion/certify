using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Certify.Locales;

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
                MessageBox.Show(Application.Current.MainWindow, SR.Update_DownloadFailed);
                System.Diagnostics.Debug.WriteLine(exp.ToString());
                return null;
            }

            if (!string.IsNullOrEmpty(updateCheck.UpdateFilePath))
            {
                if (MessageBox.Show(Application.Current.MainWindow, SR.Update_ReadyToApply, ConfigResources.AppName, MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes)
                {
                    var p = new System.Diagnostics.Process();
                    p.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                    p.StartInfo.FileName = updateCheck.UpdateFilePath;
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.RedirectStandardError = true;
                    p.StartInfo.Arguments = "/SILENT";

                    // re-verify file hash directly before execution
                    if (new Management.Util().VerifyUpdateFile(updateCheck.UpdateFilePath, updateCheck.Message.SHA256, false))
                    {
                        // execute verified file
                        p.Start();

                        Application.Current.Shutdown();
                    }
                    else
                    {
                        MessageBox.Show(Application.Current.MainWindow, "The application update failed secondary verification. A malicious process may have changed the setup file after the downloaded completed.", ConfigResources.AppName);
                    }
                }
            }
            Mouse.OverrideCursor = Cursors.Arrow;
            return updateCheck;
        }
    }
}

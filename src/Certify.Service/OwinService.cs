using System;
using System.Diagnostics;
using System.ServiceProcess;
using Certify.Management;
using Microsoft.Owin.Hosting;

namespace Certify.Service
{
    public class OwinService
    {
        private IDisposable _webApp;

        public string DiagnoseServices()
        {
            var httpStatus = "Unknown";

            try
            {
                var sc = new ServiceController("HTTP");
                switch (sc.Status)
                {
                    case ServiceControllerStatus.Running:
                        httpStatus = "Running";
                        break;
                    case ServiceControllerStatus.Stopped:
                        httpStatus = "Stopped";
                        break;
                    case ServiceControllerStatus.Paused:
                        httpStatus = "Paused";
                        break;
                    case ServiceControllerStatus.StopPending:
                        httpStatus = "Stopping";
                        break;
                    case ServiceControllerStatus.StartPending:
                        httpStatus = "Starting";
                        break;
                    default:
                        httpStatus = "Status Changing";
                        break;
                }
            }
            catch { }

            return $"System HTTP Service: {httpStatus}";
        }

        public void Start(int? portOverride = null)
        {

            var serviceConfig = SharedUtils.ServiceConfigManager.GetAppServiceConfig();

            serviceConfig.ServiceFaultMsg = "";

            if (portOverride != null)
            {
                serviceConfig.Port = (int)portOverride;
            }

            if (serviceConfig.UseHTTPS)
            {
                // ensure self signed cert available on required port
                if (InstallSelfSignedCert(serviceConfig.Host, serviceConfig.Port))
                {
                    Program.LogEvent(null, $"Local service certificate installed ok", false);
                }
                else
                {
                    Program.LogEvent(null, $"Local service certificate installed failed", false);
                }
            }

            var serviceUri = $"{(serviceConfig.UseHTTPS ? "https" : "http")}://{serviceConfig.Host}:{serviceConfig.Port}";

            try
            {
                _webApp = WebApp.Start<APIHost>(serviceUri);
                Program.LogEvent(null, $"Service API bound OK to {serviceUri}", false);
            }
            catch (Exception exp)
            {
                var httpSysStatus = DiagnoseServices();

                var msg = $"Service failed to listen on {serviceUri}. :: {httpSysStatus}";

                Program.LogEvent(exp, msg, false);

                if (serviceConfig.EnableAutoPortNegotiation)
                {
                    msg = $"Attempting to reallocate service port.";

                    Program.LogEvent(exp, msg, false);

                    // failed to listen on service uri, attempt reconfiguration of port.
                    serviceConfig.UseHTTPS = false;
                    var currentPort = serviceConfig.Port;

                    var newPort = currentPort += 2;

                    var reconfiguredServiceUri = $"{(serviceConfig.UseHTTPS ? "https" : "http")}://{serviceConfig.Host}:{newPort}";

                    try
                    {
                        // if the http listener cannot bind here then the entire service will fail to start
                        _webApp = WebApp.Start<APIHost>(reconfiguredServiceUri);

                        Program.LogEvent(null, $"Service API bound OK to {reconfiguredServiceUri}", false);

                        // if that worked, save the new port setting
                        serviceConfig.Port = newPort;

                        System.Diagnostics.Debug.WriteLine($"Service started on {reconfiguredServiceUri}.");
                    }
                    catch (Exception)
                    {
                        serviceConfig.ServiceFaultMsg = $"Service failed to listen on {serviceUri}. \r\n\r\n{httpSysStatus} \r\n\r\nEnsure the windows HTTP service is available using 'sc config http start= demand' then 'net start http' commands.";
                        throw;
                    }
                }
                else
                {

                    // port is in use, service not listening.
                    Program.LogEvent(null, $"Port negotiation not enabled. Service API failed to bind to {serviceUri}", false);
                }
            }
            finally
            {
                if (serviceConfig.ConfigStatus == Shared.ConfigStatus.Updated || serviceConfig.ConfigStatus == Shared.ConfigStatus.New)
                {
                    SharedUtils.ServiceConfigManager.StoreUpdatedAppServiceConfig(serviceConfig);
                }
            }
        }

        public void Stop()
        {
            if (_webApp != null)
            {
                _webApp.Dispose();
            }
        }

        public bool InstallSelfSignedCert(string host, int port)
        {
            var certSubject = "Certify Admin Service";
            var certStore = CertificateManager.DEFAULT_STORE_NAME;
            var currentCert = CertificateManager.GetCertificateFromStore(certSubject, certStore);

            var certUpdated = false;

            // if cert expired will need a new one
            if (currentCert != null && currentCert.NotAfter <= DateTime.Now)
            {
                CertificateManager.RemoveCertificate(currentCert, certStore);
                currentCert = null;
            }

            if (currentCert == null)
            {
                // create and install new cert
                var newCert = CertificateManager.GenerateSelfSignedCertificate(certSubject, DateTime.Now, DateTime.Now.AddYears(3), "[Certify Background API]");
                currentCert = CertificateManager.StoreCertificate(newCert, storeName: certStore);

                certUpdated = true;
            }

            // bind cert to our port

            if (currentCert != null && certUpdated)
            {
                var deleteCertBinding = "http delete sslcert ipport=0.0.0.0:" + port;
                var addCertBinding = "http add sslcert ipport=0.0.0.0:" + port + " certhash=" + currentCert.GetCertHashString() + " appid={af3a0c50-73eb-4159-a36f-82c7c77a2766}";

                var procStartInfo = new ProcessStartInfo("netsh", deleteCertBinding)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var proc = Process.Start(procStartInfo);
                proc.WaitForExit(10000);

                procStartInfo.Arguments = addCertBinding;
                proc = Process.Start(procStartInfo);
                proc.WaitForExit(10000);

            }

            return true;
        }
    }
}

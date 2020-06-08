using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Certify.Models;

namespace Certify.Providers
{
    public interface IStatusReporting
    {
        Task ReportRequestProgress(RequestProgressState status);
        Task ReportManagedCertificateUpdated(ManagedCertificate item);

    }
}

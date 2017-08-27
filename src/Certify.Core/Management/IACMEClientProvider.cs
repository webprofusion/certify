using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Certify.Management
{
    public interface IACMEClientProvider
    {
        bool AddNewRegistrationAndAcceptTOS(string email);

        string GetAcmeBaseURI();
    }
}
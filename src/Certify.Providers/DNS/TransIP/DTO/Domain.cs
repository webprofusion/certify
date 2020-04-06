using System.Collections.Generic;

namespace Certify.Providers.DNS.TransIP.DTO
{
#pragma warning disable 649
    internal struct Domain
    {
        public string name;
    }

    internal struct Domains
    {
        public List<Domain> domains;
    }
#pragma warning restore 649
}

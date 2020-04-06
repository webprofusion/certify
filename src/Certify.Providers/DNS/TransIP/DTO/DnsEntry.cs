using System.Collections.Generic;

namespace Certify.Providers.DNS.TransIP.DTO
{
#pragma warning disable 649
	internal struct DnsEntry
	{
		public string name;
		public int expire;
		public string type;
		public string content;
	}

	internal struct SingleDnsEntry
	{
		public DnsEntry dnsEntry;
	}

	internal struct DnsEntries
	{
		public List<DnsEntry> dnsEntries;
	}
#pragma warning restore 649
}

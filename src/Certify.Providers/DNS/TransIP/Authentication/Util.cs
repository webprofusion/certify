using System;

namespace Certify.Providers.DNS.TransIP.Authentication
{
    internal class Util
    {
		internal string GetUniqueId()
		{
			var ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0);
			var t = ts.TotalMilliseconds / 1000;

			var a = (int)Math.Floor(t);
			var b = (int)((t - Math.Floor(t)) * 1000000);
			var c = new Random().NextDouble() * 10;

			return a.ToString("x8") + b.ToString("x5") + c.ToString("f8", System.Globalization.CultureInfo.InvariantCulture);
		}

		internal int GetUnixEpoch()
		{
			return (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
		}
	}
}

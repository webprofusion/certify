using System.Security.Cryptography;
using System.Text;

namespace Certify.Providers.DNS.TransIP.Authentication {
	internal class DigestCreator {
		private readonly SHA512Managed _sha512;
		private readonly byte[] _asn1Header = new byte[]{
				0x30, 0x51, 0x30, 0x0d, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x02, 0x03, 0x05, 0x00, 0x04, 0x40
		};

		public DigestCreator() {
			_sha512 = new SHA512Managed();
		}

		public byte[] Create(string data) {
			var encodedData = ComputeSha512Hash(data);
			return Concat(_asn1Header, encodedData);
		}

		public byte[] ComputeSha512Hash(string data) {
			var dataBytes = Encoding.UTF8.GetBytes(data);
			return _sha512.ComputeHash(dataBytes);
		}

		public byte[] Concat(byte[] array1, byte[] array2) {
			var result = new byte[array1.Length + array2.Length];
			array1.CopyTo(result, 0);
			array2.CopyTo(result, array1.Length);
			return result;
		}
	}
}

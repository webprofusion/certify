namespace Certify.Providers.DNS.TransIP.DTO
{
#pragma warning disable 649
	internal struct LoginRequest {
		public string login;
		public string nonce;
		public bool read_only;
		public string expiration_time;
		public string label;
		public bool global_key;
	}

	internal struct LoginResult
	{
		public string token;
		public string error;
	}
#pragma warning restore 649
}

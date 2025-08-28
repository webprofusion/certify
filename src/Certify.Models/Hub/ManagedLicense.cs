namespace Certify.Models.Hub
{
    /// <summary>
    /// Managed License item for a product or service.
    /// </summary>
    /// <param name="Email"></param>
    /// <param name="ProductKey"></param>
    public class ManagedLicense : ConfigurationStoreItem
    {
        public ManagedLicense() { }
        public ManagedLicense(string email, string productKey)
        {
            Email = email;
            ProductKey = productKey;
        }

        public string Email { get; set; } = string.Empty;
        public string ProductKey { get; set; } = string.Empty;
    }
}

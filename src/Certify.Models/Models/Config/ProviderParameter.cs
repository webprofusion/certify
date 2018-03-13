namespace Certify.Models.Config
{
    public class ProviderParameter
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsPassword { get; set; }
        public bool IsRequired { get; set; }
    }
}
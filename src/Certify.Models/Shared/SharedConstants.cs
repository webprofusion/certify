namespace Certify.Models
{
    public static class SharedConstants
    {
#if _DEMO_
        public const string APPDATASUBFOLDER = "CertifyDemo";
#else
        public const string APPDATASUBFOLDER = "certify";
#endif
    }
}

namespace Certify.Models
{
    public static class SharedConstants
    {
#if _DEMO_
        public static string APPDATASUBFOLDER = "CertifyDemo";
#else
        public static string APPDATASUBFOLDER = "Certify";
#endif
    }
}

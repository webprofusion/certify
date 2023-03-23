namespace Certify.Providers.ACME.Certes
{
    public class DiagEcKey
    {
        public string kty { get; set; }
        public string crv { get; set; }
        public string x { get; set; }
        public string y { get; set; }
    }

    /// <summary>
    /// used to diagnose account key faults
    /// </summary>
    public class DiagAccountInfo
    {
        public int ID { get; set; }
        public DiagEcKey Key { get; set; }
    }
}

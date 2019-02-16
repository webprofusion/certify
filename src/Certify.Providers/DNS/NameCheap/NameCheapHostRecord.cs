namespace NameCheap
{
    /// <summary>
    /// Information about a single host.
    /// </summary>
    public class NameCheapHostRecord
    {
        public int HostId { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Address { get; set; }
        public int MxPref { get; set; }
        public int Ttl { get; set; }
    }
}

namespace KerioControlWeb.Models
{
    public class DnsHostsViewModel
    {
        public List<DnsHost> Hosts { get; set; } = new();
        public int TotalHosts { get; set; }
    }

}

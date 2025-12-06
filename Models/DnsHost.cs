namespace KerioControlWeb.Models
{
    public class DnsHost
    {
        public string Id { get; set; } = string.Empty;
        public string Ip { get; set; } = string.Empty;
        public string Hosts { get; set; } = string.Empty;       // название хоста
        public string Description { get; set; } = string.Empty;
        public bool Enabled { get; set; } = false;
    }
}

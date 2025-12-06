using System.Collections.Generic;

namespace KerioControlWeb.Models
{
    public class DashboardModel
    {
        public List<string> IpGroups { get; set; } = new List<string>();
        public List<string> UrlGroups { get; set; } = new List<string>();
        public List<string> ExtractedData { get; set; } = new List<string>();

    }
}
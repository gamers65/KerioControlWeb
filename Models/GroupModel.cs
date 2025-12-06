using System.Collections.Generic;

namespace KerioControlWeb.Models
{
    public class GroupModel
    {
        public string GroupName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Items { get; set; } = new List<string>();
        public bool IsUrlGroup { get; set; }

    }
}
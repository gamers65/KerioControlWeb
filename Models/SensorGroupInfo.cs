namespace KerioControlWeb.Models
{
    public class SensorGroupInfo
    {
        public string GroupName { get; set; } = string.Empty;
        public int ItemCount { get; set; } = 0;
    }

    public class FullGroupData
    {
        public string GroupName { get; set; } = string.Empty;
        public List<GroupItemDetail> Items { get; set; } = new List<GroupItemDetail>();
    }

    public class GroupItemDetail
    {
        public string Host { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}




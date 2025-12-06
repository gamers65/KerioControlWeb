using KerioControlWeb.Models;

public class SensorsViewModel
{
    public List<SensorGroupInfo> IpGroups { get; set; } = new();
    public List<SensorGroupInfo> UrlGroups { get; set; } = new();

    // Убедись что эти свойства правильно считают общее количество
    public int TotalIpItems => IpGroups?.Where(g => g.ItemCount > 0).Sum(g => g.ItemCount) ?? 0;
    public int TotalUrlItems => UrlGroups?.Where(g => g.ItemCount > 0).Sum(g => g.ItemCount) ?? 0;
}
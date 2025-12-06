using KerioControlWeb.Models;

namespace KerioControlWeb.Services
{
    public interface IKerioApiService
    {
        Task<bool> LoginAsync(string username, string password, string ipAddress);
        Task<(bool success, string message)> CreateGroupAsync(GroupModel group);

        // Методы для работы с токеном
        string? GetToken();
        string? GetBaseUrl();
        string LastKerioTime { get; }
        void SetAuthData(string token, string baseUrl);
        void StartKeepAlive();
        void StopKeepAlive();
        // Остальные методы...
        Task<List<string>> GetIpGroupsAsync();
        Task<List<string>> GetUrlGroupsAsync();
        // Для Sensors (полные группы)
        Task<List<GroupModel>> GetFullIpGroupsAsync();
        Task<List<GroupModel>> GetFullUrlGroupsAsync();
        Task<List<FullGroupData>> GetDetailedIpGroupsAsync();
        Task<List<FullGroupData>> GetDetailedUrlGroupsAsync();
        Task<List<DnsHost>> GetDnsHostsAsync();
        Task<bool> UpdateDnsHostsAsync(List<DnsHost> hosts);
        Task<string> GetAntivirusStatusAsync();
        Task<bool> SetAntivirusStatusAsync(bool enable);
        Task ExportGroupsToCsvAsync(string exportType);
    }
}
namespace KerioControlWeb.Services
{
    public interface ILogService
    {
        Task LogAsync(string message);
        Task<List<string>> GetLogsAsync(int lastLines = 500); // читаем последние N строк
    }
}

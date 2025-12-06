namespace KerioControlWeb.Services
{
    public interface IFileLogService
    {
        Task LogAsync(string message);
    }
}

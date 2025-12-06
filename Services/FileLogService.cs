using System.Text;

namespace KerioControlWeb.Services
{
    public class FileLogService : ILogService
    {
        private readonly string _logFile;

        public FileLogService()
        {
            // Папка Logs рядом с приложением
            string logDir = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);

            _logFile = Path.Combine(logDir, "Logs.txt");
        }

        public async Task LogAsync(string message)
        {
            string logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";
            await File.AppendAllTextAsync(_logFile, logLine);
        }

        public async Task<List<string>> GetLogsAsync(int lastLines = 500)
        {
            if (!File.Exists(_logFile))
                return new List<string>();

            var allLines = await File.ReadAllLinesAsync(_logFile, Encoding.UTF8);
            return allLines.Reverse().Take(lastLines).Reverse().ToList();
        }
    }
}

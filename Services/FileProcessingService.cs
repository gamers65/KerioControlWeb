using KerioControlWeb.Helpers;
using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace KerioControlWeb.Services
{
    public class FileProcessingService : IFileProcessingService
    {
        // Основной метод извлечения IOC из текста
        public async Task<List<string>> ExtractIocsAsync(string content)
        {
            // 1 — Удаляем пробелы, переносы и PDF-вставки
            content = Regex.Replace(content, @"\s+", "");

            // 2 — Убираем невидимые PDF-символы
            content = content.Replace("\u00A0", "")
                             .Replace("\u200B", "")
                             .Replace("\u200C", "")
                             .Replace("\u200D", "")
                             .Replace("\u2028", "")
                             .Replace("\u2029", "");

            // 3 — Восстанавливаем точки
            content = content.Replace("[.]", ".");

            // 4 — Восстанавливаем http/https
            content = content.Replace("hxxp[:]", "http://")
                             .Replace("hxxps[:]", "https://");

            // 5 — Основной вызов
            var all = RegexExtractor.ExtractAll(content);

            // 6 — Нормализация
            var filtered = all
                .Select(x => x.ToLower())
                .Distinct()
                .ToList();

            return filtered;
        }

        // Извлечение из Word
        public async Task<List<string>> ExtractFromWordAsync(IFormFile file)
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);

            string text = FileHelper.ReadWord(stream);
            return await ExtractIocsAsync(text);
        }

        // Извлечение из текстового файла
        public async Task<List<string>> ExtractFromTextAsync(IFormFile file)
        {
            using var reader = new StreamReader(file.OpenReadStream());
            string text = await reader.ReadToEndAsync();
            return await ExtractIocsAsync(text);
        }

        // Просто обработка строки
        public async Task<List<string>> ProcessTextAsync(string text)
        {
            return await ExtractIocsAsync(text);
        }
    }
}

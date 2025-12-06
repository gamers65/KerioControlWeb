using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace KerioControlWeb.Services
{
    public interface IFileProcessingService
    {
        Task<List<string>> ExtractIocsAsync(string content);
        Task<List<string>> ExtractFromWordAsync(IFormFile file);
        Task<List<string>> ExtractFromTextAsync(IFormFile file);
        Task<List<string>> ProcessTextAsync(string text);
    }
}
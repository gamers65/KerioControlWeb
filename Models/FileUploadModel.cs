using Microsoft.AspNetCore.Http;

namespace KerioControlWeb.Models
{
    public class FileUploadModel
    {
        public IFormFile File { get; set; }
    }
}
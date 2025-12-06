using System.IO;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace KerioControlWeb.Helpers
{
    public static class FileHelper
    {
        // Чтение текста из Word (.docx)
        public static string ReadWord(Stream stream)
        {
            using var mem = new MemoryStream();
            stream.CopyTo(mem);
            mem.Position = 0;

            using var wordDoc = WordprocessingDocument.Open(mem, false);
            var body = wordDoc.MainDocumentPart.Document.Body;
            return body.InnerText;
        }
    }
}

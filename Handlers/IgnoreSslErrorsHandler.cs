using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace KerioControlWeb.Handlers
{
    public class IgnoreSslErrorsHandler : HttpClientHandler
    {
        public IgnoreSslErrorsHandler()
        {
            // Игнорируем все ошибки SSL
            ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) => true;

            // Дополнительные настройки для обхода SSL проблем
            CheckCertificateRevocationList = false;

            // Разрешаем все протоколы безопасности
            SslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                          System.Security.Authentication.SslProtocols.Tls13;

            // Автоматическое сжатие
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Логируем запросы для отладки
            Console.WriteLine($"Отправка запроса: {request.Method} {request.RequestUri}");

            var response = await base.SendAsync(request, cancellationToken);

            Console.WriteLine($"Получен ответ: {(int)response.StatusCode} {response.StatusCode}");

            return response;
        }
    }
}
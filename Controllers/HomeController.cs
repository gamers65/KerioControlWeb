using Microsoft.AspNetCore.Mvc;
using KerioControlWeb.Models;
using KerioControlWeb.Services;
using KerioControlWeb.Extensions;
using Newtonsoft.Json;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using System.Text;
using KerioControlWeb.Helpers;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.IO.Compression;

namespace KerioControlWeb.Controllers
{
    public class HomeController : Controller
    {
        // Сервисы для работы с API Kerio, обработкой файлов и логами
        private readonly IKerioApiService _kerioService;
        private readonly IFileProcessingService _fileService;
        private readonly ILogService _logService;
        private readonly string _dnsBackupFile = Path.Combine(Directory.GetCurrentDirectory(), "dns_hosts_backup.txt");
        private readonly IFileProcessingService _fileProcessingService;
        private readonly ExcludeService _excludeService;
        private readonly IHttpClientFactory _httpClientFactory;


        // Конструктор для внедрения зависимостей
        public HomeController(
            IKerioApiService kerioService,
            IFileProcessingService fileService,
            ILogService logService,
            IFileProcessingService fileProcessingService,
            ExcludeService excludeService,
            IHttpClientFactory httpClientFactory
        )
        {
            _kerioService = kerioService;
            _fileService = fileService;
            _logService = logService;
            _fileProcessingService = fileProcessingService;
            _excludeService = excludeService;
            _httpClientFactory = httpClientFactory;
        }

        // Главная страница (индекс)
        public IActionResult Index()
        {
            return View();
        }

        // Обработка входа в систему (логин)
        [HttpPost]
        public async Task<IActionResult> Login(LoginModel model)
        {
            if (!ModelState.IsValid)
                return View("Index", model);
            var success = await _kerioService.LoginAsync(model.Username, model.Password, model.IpAddress);
            if (success)
            {
                // Сохраняем токен в сессии
                HttpContext.Session.SetString("IsAuthenticated", "true");
                HttpContext.Session.SetString("KerioToken", _kerioService.GetToken());
                HttpContext.Session.SetString("KerioBaseUrl", _kerioService.GetBaseUrl());
                Console.WriteLine($"✅ Токен сохранен в сессии: {_kerioService.GetToken()?.Substring(0, 10)}...");
                return RedirectToAction("Dashboard");
            }
            else
            {
                ModelState.AddModelError("", "Ошибка авторизации. Проверьте логин, пароль и IP адрес.");
                return View("Index", model);
            }
        }

        // Выход из системы (логаут)
        [HttpGet]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            // Останавливаем KeepAlive
            _kerioService.StopKeepAlive();
            Console.WriteLine("Пользователь вышел из системы");
            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> GetActualGroupsInfo()
        {
            if (HttpContext.Session.GetString("IsAuthenticated") != "true")
                return Unauthorized();

            try
            {
                var token = HttpContext.Session.GetString("KerioToken");
                var baseUrl = HttpContext.Session.GetString("KerioBaseUrl");
                _kerioService.SetAuthData(token, baseUrl);

                var ipGroupsData = await _kerioService.GetFullIpGroupsAsync();
                var urlGroupsData = await _kerioService.GetFullUrlGroupsAsync();

                // Добавляем проверку на null
                var result = new
                {
                    ipGroups = ipGroupsData?.ToDictionary(g => g.GroupName ?? "Без названия", g => g.Items?.Count ?? 0)
                                ?? new Dictionary<string, int>(),
                    urlGroups = urlGroupsData?.ToDictionary(g => g.GroupName ?? "Без названия", g => g.Items?.Count ?? 0)
                                 ?? new Dictionary<string, int>()
                };

                return Json(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка при получении актуальных данных групп: {ex.Message}");
                return Json(new { ipGroups = new Dictionary<string, int>(), urlGroups = new Dictionary<string, int>() });
            }
        }

        // Проверка аутентификации
        [HttpPost]
        public IActionResult CheckAuth()
        {
            // Здесь можно добавить дополнительную логику проверки
            return Json(new { authenticated = true });
        }

        // Дашборд (панель управления)
        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            if (HttpContext.Session.GetString("IsAuthenticated") != "true")
                return RedirectToAction("Index");
            var token = HttpContext.Session.GetString("KerioToken");
            var baseUrl = HttpContext.Session.GetString("KerioBaseUrl");
            _kerioService.SetAuthData(token, baseUrl);
            var ipGroups = await _kerioService.GetIpGroupsAsync();
            var urlGroups = await _kerioService.GetUrlGroupsAsync();
            var model = new DashboardModel
            {
                IpGroups = ipGroups,
                UrlGroups = urlGroups
            };
            return View(model);
        }

        // Получение логов
        [HttpGet]
        public async Task<IActionResult> Logs()
        {
            if (HttpContext.Session.GetString("IsAuthenticated") != "true")
                return RedirectToAction("Index");
            var logs = await _logService.GetLogsAsync();
            return View(logs); // передаём список строк в View
        }

        // Страница DNS и антивируса
        [HttpGet]
        public async Task<IActionResult> DnsAndAntivirus()
        {
            if (HttpContext.Session.GetString("IsAuthenticated") != "true")
                return RedirectToAction("Index");
            var token = HttpContext.Session.GetString("KerioToken");
            var baseUrl = HttpContext.Session.GetString("KerioBaseUrl");
            _kerioService.SetAuthData(token, baseUrl); // убедись, что _dnsService внедрен через DI
            try
            {
                // Получаем список всех DNS-хостов
                var hostsData = await _kerioService.GetDnsHostsAsync();
                // Формируем ViewModel
                var model = new DnsHostsViewModel
                {
                    Hosts = hostsData ?? new List<DnsHost>(),
                    TotalHosts = hostsData?.Count ?? 0
                };
                Console.WriteLine($"📊 Загружено DNS хостов: {model.TotalHosts}");
                return View(model);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка при загрузке DNS-хостов: {ex.Message}");
                // Возвращаем пустую модель в случае ошибки
                return View(new DnsHostsViewModel
                {
                    Hosts = new List<DnsHost>(),
                    TotalHosts = 0
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateDnsHosts([FromBody] List<DnsHost> hosts)
        {
            if (HttpContext.Session.GetString("IsAuthenticated") != "true")
                return Unauthorized();

            var token = HttpContext.Session.GetString("KerioToken");
            var baseUrl = HttpContext.Session.GetString("KerioBaseUrl");
            _kerioService.SetAuthData(token, baseUrl);

            var success = await _kerioService.UpdateDnsHostsAsync(hosts);
            return success ? Ok() : BadRequest();
        }

        [HttpPost]
        public IActionResult SaveDnsToFile([FromBody] List<DnsHost> hosts)
        {
            try
            {
                var json = JsonConvert.SerializeObject(hosts, Formatting.Indented);
                System.IO.File.WriteAllText(_dnsBackupFile, json, Encoding.UTF8);
                return Ok(new { message = "DNS Hosts сохранены в файл" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Ошибка сохранения: {ex.Message}" });
            }
        }

        [HttpGet]
        public IActionResult RestoreDnsFromFile()
        {
            try
            {
                if (!System.IO.File.Exists(_dnsBackupFile))
                    return NotFound(new { message = "Файл резервной копии не найден" });

                var json = System.IO.File.ReadAllText(_dnsBackupFile, Encoding.UTF8);
                var hosts = JsonConvert.DeserializeObject<List<DnsHost>>(json) ?? new List<DnsHost>();

                return Ok(hosts);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Ошибка восстановления: {ex.Message}" });
            }
        }

        // Страница сенсоров (группы IP и URL)
        [HttpGet]
        public async Task<IActionResult> Sensors()
        {
            if (HttpContext.Session.GetString("IsAuthenticated") != "true")
                return RedirectToAction("Index");

            var token = HttpContext.Session.GetString("KerioToken");
            var baseUrl = HttpContext.Session.GetString("KerioBaseUrl");
            _kerioService.SetAuthData(token, baseUrl);

            try
            {
                var ipGroupsData = await _kerioService.GetFullIpGroupsAsync();
                var urlGroupsData = await _kerioService.GetFullUrlGroupsAsync();

                // Добавь логирование для отладки
                Console.WriteLine($"📊 Сервер: IP групп = {ipGroupsData?.Count}, URL групп = {urlGroupsData?.Count}");
                if (urlGroupsData != null)
                {
                    foreach (var group in urlGroupsData)
                    {
                        Console.WriteLine($"🔗 URL группа: {group.GroupName} -> {group.Items?.Count} элементов");
                    }
                }

                var model = new SensorsViewModel
                {
                    IpGroups = ipGroupsData.Select(group => new SensorGroupInfo
                    {
                        GroupName = group.GroupName,
                        ItemCount = group.Items?.Count ?? 0
                    }).ToList(),
                    UrlGroups = urlGroupsData.Select(group => new SensorGroupInfo
                    {
                        GroupName = group.GroupName,
                        ItemCount = group.Items?.Count ?? 0
                    }).ToList()
                };

                Console.WriteLine($"📈 Модель: IP групп = {model.IpGroups.Count}, URL групп = {model.UrlGroups.Count}");
                Console.WriteLine($"📈 Модель: Всего IP = {model.TotalIpItems}, Всего URL = {model.TotalUrlItems}");

                return View(model);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка: {ex.Message}");
                return View(new SensorsViewModel());
            }
        }

        // Страница для пинга (анонимный доступ)
        [AllowAnonymous]
        public IActionResult Ping()
        {
            return View();
        }

        // Получение времени Kerio
        [HttpGet]
        public IActionResult GetKerioTime()
        {
            if (HttpContext.Session.GetString("IsAuthenticated") != "true")
                return Content("Недоступно");
            var kerioTime = _kerioService.LastKerioTime;
            return Content(string.IsNullOrEmpty(kerioTime) ? "Недоступно" : kerioTime);
        }

        // Получение групп IP
        [HttpGet]
        public async Task<IActionResult> GetIpGroups()
        {
            if (!HttpContext.Session.TryGetValue("IsAuthenticated", out _))
                return Unauthorized();
            var groups = await _kerioService.GetIpGroupsAsync(); // тут будет твой реальный запрос через API
            return Json(groups);
        }

        // Получение групп URL
        [HttpGet]
        public async Task<IActionResult> GetUrlGroups()
        {
            if (!HttpContext.Session.TryGetValue("IsAuthenticated", out _))
                return Unauthorized();
            var groups = await _kerioService.GetUrlGroupsAsync(); // тут будет твой реальный запрос через API
            return Json(groups);
        }

        static readonly Regex EmailRegex =
            new Regex(@"^[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}$",
            RegexOptions.IgnoreCase);

        // ❗ Фильтр неправильных доменных частей
        bool IsInvalidDomain(string x)
        {
            var parts = x.Split('.');
            return parts.Any(p => p.StartsWith("-") || p.EndsWith("-"));
        }

        [HttpPost]
        public async Task<IActionResult> UploadFile(FileUploadModel model)
        {
            if (model.File == null)
                return Json(new { success = false, message = "Файл не выбран" });

            try
            {
                using var ms = new MemoryStream();
                await model.File.CopyToAsync(ms);

                var client = _httpClientFactory.CreateClient("PythonIocService");

                var form = new MultipartFormDataContent();
                form.Add(new ByteArrayContent(ms.ToArray()), "file", model.File.FileName);

                var resp = await client.PostAsync("/extract", form);
                var json = await resp.Content.ReadAsStringAsync();

                var py = JsonConvert.DeserializeObject<PythonExtractResponse>(json);

                if (py == null || !py.success)
                    return Json(new { success = false, message = py?.message ?? "Ошибка Python сервиса" });


                // -------------------------------
                //   ФИЛЬТРАЦИЯ IOC
                // -------------------------------

                string[] badExt = {
                    ".exe",".bat",".cmd",".scr",".pif",".msi",".msp",".dll",".sys",
                    ".vbs",".vbe",".js",".jse",".wsf",".wsh",".ps1",".psm1",".reg",

                    ".zip",".rar",".7z",".gz",".tar",".tgz",".bz2",".xz",

                    ".php",".html",".htm",".asp",".aspx",".jsp",".css",".js",
                    ".xml",".xhtml",".svg",".json",

                    ".pdf",
                    ".doc",".docx",".xls",".xlsx",".ppt",".pptx",
                    ".rtf",".odt",".ods",".odp",

                    ".png",".jpg",".jpeg",".gif",".bmp",".tif",".tiff",".ico",".webp",

                    ".mp4",".avi",".mkv",".mov",".wmv",".flv",".mp3",".wav",".aac",".ogg",

                    ".ttf",".otf",".woff",".woff2",".eot",".afm",

                    ".bin",".dat",".iso",".img",".vdf",

                    ".csv.tmp",".temp",".tmp",".bak",
                    ".log",".md",".yml",".yaml",".ini",".cfg",".lnk",".hta",".gp",".lab",
                };

                var extracted = py.data

                    // ❌ Удаляем мусорные расширения
                    .Where(x => !badExt.Any(ext => x.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))

                    // ❌ Удаляем email
                    .Where(x => !EmailRegex.IsMatch(x))

                    // ❌ Убираем домены с частями, начинающимися/заканчивающимися на "-"
                    .Where(x => !IsInvalidDomain(x))

                    // ❌ Удаляем короткое мусорное
                    .Where(x => x.Length > 3)

                    // ❌ Убираем хвосты от криво склеенных URL
                    .Where(x => !x.Contains("httphttp"))
                    .Where(x => !x.Contains(".pnghttp"))
                    .Where(x => !x.Contains(".jpghttp"))
                    .Where(x => !x.Contains(".gifhttp"))

                    .Select(x => x.Trim().ToLowerInvariant())

                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                // глобальные исключения
                var excluded = _excludeService.ExcludedItems;
                extracted = extracted.Where(x => !excluded.Contains(x)).ToList();


                return Json(new
                {
                    success = true,
                    count = extracted.Count,
                    data = extracted
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Ошибка: " + ex.Message });
            }
        }


        public class PythonExtractResponse
        {
            public bool success { get; set; }
            public int count { get; set; }
            public List<string> data { get; set; }
            public string message { get; set; }
        }

        [HttpPost]
        public IActionResult AddExclude([FromBody] string item)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(item))
                {
                    var trimmedItem = item.Trim();

                    // Проверяем, не существует ли уже такое исключение
                    if (_excludeService.Contains(trimmedItem))
                    {
                        return Json(new { success = false, message = "Элемент уже существует в исключениях" });
                    }

                    _excludeService.Add(trimmedItem);

                    // Логируем добавление
                    Console.WriteLine($"✅ Добавлено в исключения: {trimmedItem}");

                    return Json(new { success = true, message = "Элемент успешно добавлен" });
                }

                return Json(new { success = false, message = "Пустой элемент" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка при добавлении исключения: {ex.Message}");
                return Json(new { success = false, message = $"Ошибка при сохранении: {ex.Message}" });
            }
        }

        [HttpGet]
        public IActionResult GetGlobalExcludes()
        {
            try
            {
                // Получаем все исключения из сервиса
                var excludes = _excludeService.ExcludedItems.ToList();
                return Json(excludes);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка при получении глобальных исключений: {ex.Message}");
                return Json(new List<string>());
            }
        }

        [HttpPost]
        public IActionResult RemoveGlobalExclude([FromBody] string item)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(item))
                {
                    var trimmedItem = item.Trim();

                    // Удаляем из сервиса (нужно добавить метод Remove в ExcludeService)
                    if (_excludeService.Remove(trimmedItem))
                    {
                        Console.WriteLine($"🗑️ Удалено из глобальных исключений: {trimmedItem}");
                        return Json(new { success = true, message = "Элемент успешно удален" });
                    }
                    else
                    {
                        return Json(new { success = false, message = "Элемент не найден в исключениях" });
                    }
                }

                return Json(new { success = false, message = "Пустой элемент" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка при удалении глобального исключения: {ex.Message}");
                return Json(new { success = false, message = $"Ошибка при удалении: {ex.Message}" });
            }
        }

        [HttpPost]
        public IActionResult ClearAllExcludes()
        {
            try
            {
                // Очищаем все исключения (нужно добавить метод Clear в ExcludeService)
                _excludeService.Clear();
                Console.WriteLine("🧹 Все глобальные исключения очищены");
                return Json(new { success = true, message = "Все исключения успешно очищены" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка при очистке исключений: {ex.Message}");
                return Json(new { success = false, message = $"Ошибка при очистке: {ex.Message}" });
            }
        }

        [HttpPost]
        public IActionResult RemoveExclude([FromBody] string item)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(item))
                {
                    var trimmedItem = item.Trim();

                    // Для удаления нужно добавить метод Remove в ExcludeService
                    // _excludeService.Remove(trimmedItem);

                    Console.WriteLine($"🗑️ Удалено из исключений: {trimmedItem}");
                    return Json(new { success = true, message = "Элемент успешно удален" });
                }

                return Json(new { success = false, message = "Пустой элемент" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка при удалении исключения: {ex.Message}");
                return Json(new { success = false, message = $"Ошибка при удалении: {ex.Message}" });
            }
        }

        // Создание группы
        [HttpPost]
        public async Task<IActionResult> CreateGroup([FromBody] GroupModel model)
        {
            try
            {
                // Логируем начало обработки
                Console.WriteLine($"🟡 Начало обработки запроса для группы: {model.GroupName}");
                // Получаем токен из сессии
                var token = HttpContext.Session.GetString("KerioToken");
                var baseUrl = HttpContext.Session.GetString("KerioBaseUrl");
                Console.WriteLine($"🔍 Токен из сессии: {(string.IsNullOrEmpty(token) ? "ОТСУТСТВУЕТ" : "ПРИСУТСТВУЕТ")}");
                if (string.IsNullOrEmpty(token))
                {
                    Console.WriteLine("🔴 Токен отсутствует, возвращаем ошибку");
                    return Json(new { success = false, message = "Токен авторизации отсутствует. Пожалуйста, войдите снова." });
                }
                // Устанавливаем токен в сервис
                _kerioService.SetAuthData(token, baseUrl);
                // Создаем группу
                var (success, message) = await _kerioService.CreateGroupAsync(model);
                Console.WriteLine($"🟢 Завершение обработки запроса для группы: {model.GroupName}");
                return Json(new { success, message });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🔴 Исключение в контроллере: {ex.Message}");
                return Json(new
                {
                    success = false,
                    message = $"Внутренняя ошибка: {ex.Message}"
                });
            }
        }

        // Выполнение пинга через процесс
        [HttpPost]
        public async Task<IActionResult> DoPing(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return Json(new { success = false, output = "Введите IP или домен!" });
            try
            {
                var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = "ping";
                process.StartInfo.Arguments = $"{host} -n 4";
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                string result = await process.StandardOutput.ReadToEndAsync();
                return Json(new { success = true, output = result });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, output = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetGroupItemCount(string groupName, bool isUrlGroup)
        {
            if (HttpContext.Session.GetString("IsAuthenticated") != "true")
                return Unauthorized();

            try
            {
                var token = HttpContext.Session.GetString("KerioToken");
                var baseUrl = HttpContext.Session.GetString("KerioBaseUrl");
                _kerioService.SetAuthData(token, baseUrl);

                int count = 0;
                if (isUrlGroup)
                {
                    var urlGroups = await _kerioService.GetFullUrlGroupsAsync();
                    var group = urlGroups.FirstOrDefault(g => g.GroupName == groupName);
                    count = group?.Items?.Count ?? 0;
                }
                else
                {
                    var ipGroups = await _kerioService.GetFullIpGroupsAsync();
                    var group = ipGroups.FirstOrDefault(g => g.GroupName == groupName);
                    count = group?.Items?.Count ?? 0;
                }

                return Json(count);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка при получении количества элементов: {ex.Message}");
                return Json(0);
            }
        }

        // Пинг сервера (анонимный доступ)
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> PingServer(string address)
        {
            if (string.IsNullOrEmpty(address))
                return BadRequest("Адрес не указан");
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                var reply = await ping.SendPingAsync(address, 1000); // таймаут 1 сек
                if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                {
                    return Content($"Ответ от {reply.Address}: число байт={reply.Buffer.Length} время={reply.RoundtripTime}мс TTL={reply.Options?.Ttl}");
                }
                else
                {
                    return Content($"Сервер недоступен: {reply.Status}");
                }
            }
            catch (Exception ex)
            {
                return Content($"Ошибка: {ex.Message}");
            }
        }


        [HttpGet]
        public async Task<IActionResult> DownloadUrlGroups([FromQuery] List<string> groupNames, [FromQuery] bool includeDescription = false)
        {
            if (HttpContext.Session.GetString("IsAuthenticated") != "true")
                return Unauthorized();

            try
            {
                var token = HttpContext.Session.GetString("KerioToken");
                var baseUrl = HttpContext.Session.GetString("KerioBaseUrl");
                _kerioService.SetAuthData(token, baseUrl);

                // Используем метод для получения детальных URL данных
                var urlGroupsData = await _kerioService.GetDetailedUrlGroupsAsync();

                Console.WriteLine($"📊 Получено детальных URL групп для скачивания: {urlGroupsData?.Count}");

                using var memoryStream = new MemoryStream();
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                {
                    foreach (var groupName in groupNames)
                    {
                        Console.WriteLine($"🔍 Обрабатываем URL группу: {groupName}");

                        // Ищем группу в полученных данных
                        var targetGroup = urlGroupsData?.FirstOrDefault(g => g.GroupName == groupName);

                        if (targetGroup != null)
                        {
                            Console.WriteLine($"✅ Найдена URL группа: {targetGroup.GroupName}, элементов: {targetGroup.Items?.Count}");

                            // Создаем CSV контент
                            var csvContent = GenerateCsvFromDetailedGroup(targetGroup, includeDescription);
                            var fileName = $"{SanitizeFileName(groupName)}.csv";
                            var entry = archive.CreateEntry(fileName, CompressionLevel.Fastest);

                            using var entryStream = entry.Open();
                            using var writer = new StreamWriter(entryStream, Encoding.UTF8);
                            await writer.WriteAsync(csvContent);

                            Console.WriteLine($"💾 Создан файл: {fileName}");
                        }
                        else
                        {
                            Console.WriteLine($"❌ URL группа '{groupName}' не найдена");

                            // Создаем пустой файл для отсутствующей группы
                            var fileName = $"{SanitizeFileName(groupName)}.csv";
                            var entry = archive.CreateEntry(fileName, CompressionLevel.Fastest);

                            using var entryStream = entry.Open();
                            using var writer = new StreamWriter(entryStream, Encoding.UTF8);
                            var header = includeDescription ? "URL,Description" : "URL";
                            await writer.WriteAsync(header + "\n");
                        }
                    }
                }

                memoryStream.Seek(0, SeekOrigin.Begin);
                return File(memoryStream.ToArray(), "application/zip", "url_groups.zip");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка при создании архива URL групп: {ex.Message}");
                return BadRequest($"Ошибка при создании архива: {ex.Message}");
            }
        }

        [HttpGet]
        public async Task<IActionResult> DownloadIpGroups([FromQuery] List<string> groupNames, [FromQuery] bool includeDescription = false)
        {
            if (HttpContext.Session.GetString("IsAuthenticated") != "true")
                return Unauthorized();

            try
            {
                var token = HttpContext.Session.GetString("KerioToken");
                var baseUrl = HttpContext.Session.GetString("KerioBaseUrl");
                _kerioService.SetAuthData(token, baseUrl);

                // Используем новый метод для получения детальных данных
                var ipGroupsData = await _kerioService.GetDetailedIpGroupsAsync();

                Console.WriteLine($"📊 Получено детальных IP групп для скачивания: {ipGroupsData?.Count}");

                using var memoryStream = new MemoryStream();
                using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                {
                    foreach (var groupName in groupNames)
                    {
                        Console.WriteLine($"🔍 Обрабатываем группу: {groupName}");

                        // Ищем группу в полученных данных
                        var targetGroup = ipGroupsData?.FirstOrDefault(g => g.GroupName == groupName);

                        if (targetGroup != null)
                        {
                            Console.WriteLine($"✅ Найдена группа: {targetGroup.GroupName}, элементов: {targetGroup.Items?.Count}");

                            // Создаем CSV контент
                            var csvContent = GenerateCsvFromDetailedGroup(targetGroup, includeDescription);
                            var fileName = $"{SanitizeFileName(groupName)}.csv";
                            var entry = archive.CreateEntry(fileName, CompressionLevel.Fastest);

                            using var entryStream = entry.Open();
                            using var writer = new StreamWriter(entryStream, Encoding.UTF8);
                            await writer.WriteAsync(csvContent);

                            Console.WriteLine($"💾 Создан файл: {fileName}");
                        }
                        else
                        {
                            Console.WriteLine($"❌ Группа '{groupName}' не найдена");

                            // Создаем пустой файл для отсутствующей группы
                            var fileName = $"{SanitizeFileName(groupName)}.csv";
                            var entry = archive.CreateEntry(fileName, CompressionLevel.Fastest);

                            using var entryStream = entry.Open();
                            using var writer = new StreamWriter(entryStream, Encoding.UTF8);
                            var header = includeDescription ? "Address,Description" : "Address";
                            await writer.WriteAsync(header + "\n");
                        }
                    }
                }

                memoryStream.Seek(0, SeekOrigin.Begin);
                return File(memoryStream.ToArray(), "application/zip", "ip_groups.zip");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка при создании архива: {ex.Message}");
                return BadRequest($"Ошибка при создании архива: {ex.Message}");
            }
        }

        private string GenerateCsvFromDetailedGroup(FullGroupData group, bool includeDescription)
        {
            var sb = new StringBuilder();

            if (includeDescription)
            {
                sb.AppendLine("Address,Description");
            }
            else
            {
                sb.AppendLine("Address");
            }

            try
            {
                foreach (var item in group.Items)
                {
                    if (includeDescription)
                    {
                        var description = item.Description?.Replace("\"", "\"\"") ?? "";
                        sb.AppendLine($"{item.Host},\"{description}\"");
                    }
                    else
                    {
                        sb.AppendLine(item.Host);
                    }
                }

                Console.WriteLine($"📄 Сгенерирован CSV с {group.Items.Count} элементами для группы {group.GroupName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка при генерации CSV: {ex.Message}");
            }

            return sb.ToString();
        }

        private string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "unnamed";

            var invalidChars = Path.GetInvalidFileNameChars();
            return string.Concat(fileName.Where(ch => !invalidChars.Contains(ch)));
        }



    }
}
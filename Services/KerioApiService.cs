using System.Text;
using Newtonsoft.Json;
using KerioControlWeb.Models;
using System.Runtime.InteropServices.JavaScript;
using Newtonsoft.Json.Linq;

namespace KerioControlWeb.Services
{
    public class KerioApiService : IKerioApiService
    {
        private readonly HttpClient _httpClient;
        private string? _authToken;
        private string? _apiBaseUrl;
        private readonly ILogService _logService;
        private Timer _keepAliveTimer;
        private string _lastKerioTime = string.Empty;
        public string LastKerioTime => _lastKerioTime;

        public KerioApiService(ILogService logService)
        {
            // Создаем HttpClient с игнорированием SSL ошибок
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) => true
            };

            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _logService = logService;

        }

        public void StopKeepAlive()
        {
            _keepAliveTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _keepAliveTimer?.Dispose();
            _keepAliveTimer = null;
            Console.WriteLine("KeepAlive остановлен");
        }

        public void StartKeepAlive()
        {
            async Task UpdateKerioTime()
            {
                try
                {
                    if (!string.IsNullOrEmpty(_authToken))
                    {
                        var result = await SendRequestAsync("SystemConfig.getDateTime", new { });

                        var config = result["result"]?["config"];
                        if (config != null)
                        {
                            var date = config["date"];
                            var time = config["time"];
                            _lastKerioTime = $"{date["year"]:D4}-{date["month"]:D2}-{date["day"]:D2} " +
                                             $"{time["hour"]:D2}:{time["min"]:D2}:{time["sec"]:D2}";
                        }

                        Console.WriteLine($"🔄 KeepAlive: обновили сессию, последняя синхронизация: {_lastKerioTime}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Ошибка KeepAlive: {ex.Message}");
                }
            }

            // Первый вызов сразу после авторизации
            _ = UpdateKerioTime();

            // Таймер каждые 4 минуты
            _keepAliveTimer = new Timer(async _ => await UpdateKerioTime(), null,
                                        TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(4));
        }

        private async Task<JObject> SendRequestAsync(string method, object parameters)
        {
            var requestObject = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = method,
                @params = parameters
            };

            string json = JsonConvert.SerializeObject(requestObject);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, _apiBaseUrl);
            httpRequest.Headers.Add("X-Token", _authToken);
            httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

            Console.WriteLine($"➡️ KeepAlive запрос: {json}");

            var response = await _httpClient.SendAsync(httpRequest);
            string result = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"⬅️ KeepAlive ответ: {result}");

            return JObject.Parse(result);
        }


        public async Task<string> SendRawRequestAsync(string requestJson)
        {
            if (string.IsNullOrEmpty(_apiBaseUrl) || _httpClient == null)
                throw new InvalidOperationException("Сервис не авторизован или HttpClient не инициализирован.");

            var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(_apiBaseUrl, content);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<bool> LoginAsync(string username, string password, string ipAddress)
        {
            try
            {
                _apiBaseUrl = $"https://{ipAddress}:4081/admin/api/jsonrpc/";

                Console.WriteLine($"Попытка подключения к {_apiBaseUrl}");

                var loginRequest = new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "Session.login",
                    @params = new
                    {
                        userName = username,
                        password = password,
                        application = new
                        {
                            name = "Web App",
                            vendor = "Kerio",
                            version = "1.0"
                        }
                    }
                };

                var json = JsonConvert.SerializeObject(loginRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                Console.WriteLine($"Отправка запроса авторизации");

                var response = await _httpClient.PostAsync(_apiBaseUrl, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"Ответ авторизации: {responseContent}");

                if (response.IsSuccessStatusCode)
                {
                    var data = JsonConvert.DeserializeObject<dynamic>(responseContent);

                    if (data?.result?.token != null)
                    {
                        _authToken = data.result.token;
                        Console.WriteLine("✅ Авторизация успешна, получен токен");
                        StartKeepAlive();
                        return true;
                    }
                }

                Console.WriteLine($"❌ Ошибка авторизации: {responseContent}");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка авторизации: {ex.Message}");
                return false;
            }
        }

        public async Task<(bool success, string message)> CreateGroupAsync(GroupModel group)
        {
            try
            {
                if (string.IsNullOrEmpty(_authToken))
                {
                    await _logService.LogAsync($"❌ Токен авторизации отсутствует");
                    return (false, "Токен авторизации отсутствует");
                }

                await _logService.LogAsync($"🔄 Создание группы: {group.GroupName}");
                await _logService.LogAsync($"📋 Всего элементов: {group.Items.Count}");
                Console.WriteLine($"🔄 Создание группы: {group.GroupName}");
                Console.WriteLine($"📋 Всего элементов: {group.Items.Count}");

                string createMethod = group.IsUrlGroup ? "UrlGroups.create" : "IpAddressGroups.create";
                string applyMethod = group.IsUrlGroup ? "UrlGroups.apply" : "IpAddressGroups.apply";

                bool groupExists = await CheckGroupExists(group.GroupName, group.IsUrlGroup);
                string existMsg = groupExists
                    ? $"ℹ️ Группа '{group.GroupName}' уже существует"
                    : $"ℹ️ Группа '{group.GroupName}' будет создана";
                Console.WriteLine(existMsg);
                await _logService.LogAsync(existMsg);

                int batchSize = 10;
                int delayMs = 1000;

                int totalCreated = 0;
                int totalDuplicates = 0;
                List<string> totalErrors = new();
                HashSet<string> addedItems = new();

                var batches = group.Items
                    .Select((value, index) => new { value, index })
                    .GroupBy(x => x.index / batchSize)
                    .Select(g => g.Select(v => v.value).ToList())
                    .ToList();

                string batchesMsg = $"📦 Всего порций: {batches.Count}";
                Console.WriteLine(batchesMsg);
                await _logService.LogAsync(batchesMsg);

                foreach (var batch in batches)
                {
                    string batchMsg = $"➡️ Отправка порции ({batch.Count} элементов): {string.Join(", ", batch)}";
                    Console.WriteLine(batchMsg);
                    await _logService.LogAsync(batchMsg);

                    var elements = batch.Select(item => new
                    {
                        groupName = group.GroupName,
                        type = group.IsUrlGroup ? "Url" : "Host",
                        url = group.IsUrlGroup ? item : null,
                        host = group.IsUrlGroup ? null : item,
                        description = group.Description,
                        isRegex = group.IsUrlGroup ? false : (bool?)null,
                        enabled = true
                    }).ToArray();

                    var createRequest = new
                    {
                        jsonrpc = "2.0",
                        id = 1,
                        method = createMethod,
                        @params = new { groups = elements }
                    };

                    var reqJson = JsonConvert.SerializeObject(createRequest);
                    var httpReq = new HttpRequestMessage(HttpMethod.Post, _apiBaseUrl);
                    httpReq.Headers.Add("X-Token", _authToken);
                    httpReq.Content = new StringContent(reqJson, Encoding.UTF8, "application/json");

                    var resp = await _httpClient.SendAsync(httpReq);
                    string raw = await resp.Content.ReadAsStringAsync();
                    Console.WriteLine($"📥 Ответ API: {raw}");
                    await _logService.LogAsync($"📥 Ответ API: {raw}");

                    if (!resp.IsSuccessStatusCode)
                    {
                        string errMsg = $"HTTP ошибка: {resp.StatusCode}";
                        await _logService.LogAsync($"❌ {errMsg}");
                        return (false, errMsg);
                    }

                    var data = JsonConvert.DeserializeObject<dynamic>(raw);
                    if (data?.error != null)
                    {
                        string errMsg = $"Ошибка API: {data.error.message}";
                        await _logService.LogAsync($"❌ {errMsg}");
                        return (false, errMsg);
                    }

                    var result = data?.result;
                    if (result == null)
                    {
                        string errMsg = "Неверный формат ответа";
                        await _logService.LogAsync($"❌ {errMsg}");
                        return (false, errMsg);
                    }

                    // Обработка результатов
                    if (result.result != null)
                    {
                        foreach (var r in result.result)
                        {
                            int idx = r.inputIndex != null ? (int)r.inputIndex : -1;
                            if (idx >= 0 && idx < batch.Count)
                            {
                                string item = batch[idx];
                                if (!addedItems.Contains(item))
                                {
                                    addedItems.Add(item);
                                    totalCreated++;
                                }
                                else
                                {
                                    totalDuplicates++;
                                }
                            }
                        }
                    }

                    // Обработка ошибок API
                    if (result.errors != null)
                    {
                        foreach (var err in result.errors)
                        {
                            string code = err.code?.ToString() ?? "unknown";
                            int idx = err.inputIndex != null ? (int)err.inputIndex : -1;
                            string badItem = idx >= 0 && idx < batch.Count ? batch[idx] : $"index {idx}";

                            switch (code)
                            {
                                case "1001":
                                    Console.WriteLine("ℹ️ Группа уже существует");
                                    await _logService.LogAsync("ℹ️ Группа уже существует");
                                    groupExists = true;
                                    foreach (var item in batch)
                                    {
                                        if (!addedItems.Contains(item))
                                        {
                                            totalDuplicates++;
                                            addedItems.Add(item);
                                        }
                                    }
                                    break;

                                case "1002":
                                case "2001":
                                case "1003":
                                    if (!addedItems.Contains(badItem))
                                    {
                                        totalDuplicates++;
                                        addedItems.Add(badItem);
                                    }
                                    string dupMsg = $"⚠️ Дубликат: {badItem}";
                                    Console.WriteLine(dupMsg);
                                    await _logService.LogAsync(dupMsg);
                                    break;

                                case "3001":
                                case "3002":
                                case "4001":
                                    string formatErr = $"❌ Ошибка формата: {badItem}";
                                    totalErrors.Add(formatErr);
                                    Console.WriteLine(formatErr);
                                    await _logService.LogAsync(formatErr);
                                    break;

                                default:
                                    string otherErr = $"Ошибка: {err.message}";
                                    totalErrors.Add(otherErr);
                                    await _logService.LogAsync(otherErr);
                                    break;
                            }
                        }
                    }

                    string createdMsg = $"Создано в порции: {(result.result != null ? result.result.Count : 0)}";
                    Console.WriteLine(createdMsg);
                    await _logService.LogAsync(createdMsg);

                    // Apply
                    if (result.result != null && result.result.Count > 0)
                    {
                        string applyMsg = $"💾 Применяем {applyMethod}...";
                        Console.WriteLine(applyMsg);
                        await _logService.LogAsync(applyMsg);

                        var applyRequest = new
                        {
                            jsonrpc = "2.0",
                            id = 2,
                            method = applyMethod,
                            @params = new { }
                        };

                        var applyJson = JsonConvert.SerializeObject(applyRequest);
                        var applyReq = new HttpRequestMessage(HttpMethod.Post, _apiBaseUrl);
                        applyReq.Headers.Add("X-Token", _authToken);
                        applyReq.Content = new StringContent(applyJson, Encoding.UTF8, "application/json");

                        var applyResp = await _httpClient.SendAsync(applyReq);
                        string applyRaw = await applyResp.Content.ReadAsStringAsync();
                        Console.WriteLine($"📥 Ответ применения: {applyRaw}");
                        await _logService.LogAsync($"📥 Ответ применения: {applyRaw}");

                        // Подтверждаем конфигурацию после применения
                        bool confirmed = await ConfirmConfigAsync();
                        if (!confirmed)
                        {
                            string errMsg = "Не удалось подтвердить конфигурацию после создания группы";
                            await _logService.LogAsync(errMsg);
                            return (false, errMsg);
                        }
                    }

                    string pauseMsg = $"⏳ Пауза {delayMs} мс перед следующей порцией...";
                    Console.WriteLine(pauseMsg);
                    await _logService.LogAsync(pauseMsg);
                    await Task.Delay(delayMs);
                }

                if (totalErrors.Count > 0)
                {
                    string errSummary = $"❌ Ошибки: {string.Join("; ", totalErrors)}";
                    await _logService.LogAsync(errSummary);
                    return (false, errSummary);
                }

                string finalMsg = groupExists
                    ? $"Группа '{group.GroupName}' существовала, добавлено {totalCreated}, дубликатов {totalDuplicates}"
                    : $"Группа '{group.GroupName}' создана: добавлено {totalCreated}, дубликатов {totalDuplicates}";

                Console.WriteLine($"🏁 Итог: {finalMsg}");
                await _logService.LogAsync($"🏁 Итог: {finalMsg}");
                Console.WriteLine("----------------------------------------------------------------------------");
                await _logService.LogAsync("----------------------------------------------------------------------------");

                return (true, finalMsg);
            }
            catch (Exception ex)
            {
                string excMsg = $"❌ Exception: {ex}";
                Console.WriteLine(excMsg);
                await _logService.LogAsync(excMsg);
                return (false, $"Ошибка: {ex.Message}");
            }
        }

        public async Task<List<DnsHost>> GetDnsHostsAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_authToken) || string.IsNullOrEmpty(_apiBaseUrl))
                    throw new InvalidOperationException("Сервис не авторизован");

                var getHostsRequest = new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "Dns.getHosts",
                    @params = new { }
                };

                var requestJson = JsonConvert.SerializeObject(getHostsRequest);
                var httpReq = new HttpRequestMessage(HttpMethod.Post, _apiBaseUrl);
                httpReq.Headers.Add("X-Token", _authToken);
                httpReq.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                Console.WriteLine($"📤 Запрос к API DNS: {requestJson}");

                var response = await _httpClient.SendAsync(httpReq);
                var content = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"📥 Ответ API DNS: {content}");

                var responseData = JsonConvert.DeserializeObject<dynamic>(content);

                if (responseData?.error != null)
                {
                    Console.WriteLine($"❌ Ошибка GetDnsHostsAsync: {responseData.error.message}");
                    return new List<DnsHost>();
                }

                var result = new List<DnsHost>();

                if (responseData?.result?.hosts != null)
                {
                    foreach (var record in responseData.result.hosts)
                    {
                        try
                        {
                            result.Add(new DnsHost
                            {
                                Id = record.id,
                                Enabled = record.enabled,
                                Ip = record.ip,
                                Hosts = record.hosts,
                                Description = record.description
                            });
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ Ошибка обработки записи DNS: {ex.Message}");
                        }
                    }

                    Console.WriteLine($"📊 Всего DNS хостов: {result.Count}");
                }
                else
                {
                    Console.WriteLine("ℹ️ В ответе API DNS нет hosts");
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка в GetDnsHostsAsync: {ex.Message}");
                return new List<DnsHost>();
            }
        }

        public async Task<bool> UpdateDnsHostsAsync(List<DnsHost> hosts)
        {
            if (string.IsNullOrEmpty(_authToken) || string.IsNullOrEmpty(_apiBaseUrl))
                throw new InvalidOperationException("Сервис не авторизован");

            var batchRequest = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "Batch.run",
                @params = new
                {
                    commandList = new[]
                    {
                new
                {
                    method = "Dns.setHosts",
                    @params = new
                    {
                        hosts = hosts.Select(h => new
                        {
                            id = h.Id,
                            ip = h.Ip,
                            hosts = h.Hosts,
                            description = h.Description,
                            enabled = h.Enabled
                        }).ToArray()
                    }
                }
            }
                }
            };

            var requestJson = JsonConvert.SerializeObject(batchRequest);
            var httpReq = new HttpRequestMessage(HttpMethod.Post, _apiBaseUrl);
            httpReq.Headers.Add("X-Token", _authToken);
            httpReq.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(httpReq);
            var content = await response.Content.ReadAsStringAsync();
            var responseData = JsonConvert.DeserializeObject<dynamic>(content);

            if (responseData?.error != null)
            {
                Console.WriteLine($"❌ Ошибка UpdateDnsHostsAsync: {responseData.error.message}");
                return false;
            }

            Console.WriteLine("✅ DNS Hosts успешно обновлены");
            return true;
        }




        private async Task<bool> ConfirmConfigAsync()
        {
            try
            {
                // 1️⃣ Получаем timestamp конфигурации
                var getTimestampRequest = new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "Session.getConfigTimestamp",
                    @params = new { }
                };

                var getTsJson = JsonConvert.SerializeObject(getTimestampRequest);
                var getTsReq = new HttpRequestMessage(HttpMethod.Post, _apiBaseUrl);
                getTsReq.Headers.Add("X-Token", _authToken);
                getTsReq.Content = new StringContent(getTsJson, Encoding.UTF8, "application/json");

                var tsResponse = await _httpClient.SendAsync(getTsReq);
                tsResponse.EnsureSuccessStatusCode();
                var tsContent = await tsResponse.Content.ReadAsStringAsync();
                var tsData = JsonConvert.DeserializeObject<dynamic>(tsContent);

                int timestamp = tsData.result.clientTimestampList[0].timestamp;

                // 2️⃣ Подтверждаем конфигурацию
                var confirmRequest = new
                {
                    jsonrpc = "2.0",
                    id = 2,
                    method = "Session.confirmConfig",
                    @params = new
                    {
                        clientTimestampList = new[]
                        {
                    new { name = "config", timestamp = timestamp }
                }
                    }
                };

                var confirmJson = JsonConvert.SerializeObject(confirmRequest);
                var confirmReq = new HttpRequestMessage(HttpMethod.Post, _apiBaseUrl);
                confirmReq.Headers.Add("X-Token", _authToken);
                confirmReq.Content = new StringContent(confirmJson, Encoding.UTF8, "application/json");

                var confirmResponse = await _httpClient.SendAsync(confirmReq);
                confirmResponse.EnsureSuccessStatusCode();
                var confirmContent = await confirmResponse.Content.ReadAsStringAsync();
                var confirmData = JsonConvert.DeserializeObject<dynamic>(confirmContent);

                bool confirmed = confirmData.result?.confirmed ?? false;
                if (!confirmed)
                {
                    Console.WriteLine("❌ Не удалось подтвердить конфигурацию");
                    return false;
                }

                // 3️⃣ Сбрасываем сессию
                var resetRequest = new
                {
                    jsonrpc = "2.0",
                    id = 3,
                    method = "Session.reset",
                    @params = new { }
                };

                var resetJson = JsonConvert.SerializeObject(resetRequest);
                var resetReq = new HttpRequestMessage(HttpMethod.Post, _apiBaseUrl);
                resetReq.Headers.Add("X-Token", _authToken);
                resetReq.Content = new StringContent(resetJson, Encoding.UTF8, "application/json");

                var resetResponse = await _httpClient.SendAsync(resetReq);
                resetResponse.EnsureSuccessStatusCode();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка ConfirmConfigAsync: {ex.Message}");
                return false;
            }
        }


        // НОВЫЙ МЕТОД: проверка существования группы
        private async Task<bool> CheckGroupExists(string groupName, bool isUrlGroup)
        {
            try
            {
                var method = isUrlGroup ? "UrlGroups.get" : "IpAddressGroups.get";

                var getRequest = new
                {
                    jsonrpc = "2.0",
                    id = 100,
                    method = method,
                    @params = new
                    {
                        query = new
                        {
                            orderBy = new[] { new { columnName = "groupName", direction = "Asc" } }
                        }
                    }
                };

                var getJson = JsonConvert.SerializeObject(getRequest);
                var request = new HttpRequestMessage(HttpMethod.Post, _apiBaseUrl);
                request.Headers.Add("X-Token", _authToken);
                request.Content = new StringContent(getJson, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var data = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    if (data?.result?.list != null)
                    {
                        foreach (var group in data.result.list)
                        {
                            if (group.groupName == groupName)
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка при проверке существования группы: {ex.Message}");
                return false; // В случае ошибки считаем что группы нет
            }
        }

        public string? GetToken() => _authToken;
        public string? GetBaseUrl() => _apiBaseUrl;

        public void SetAuthData(string token, string baseUrl)
        {
            _authToken = token;
            _apiBaseUrl = baseUrl;
            Console.WriteLine($"🔄 Установлены данные авторизации, токен: {token?.Substring(0, 10)}...");
        }

        // Остальные методы...
        public async Task<List<string>> GetIpGroupsAsync()
        {
            if (string.IsNullOrEmpty(_authToken) || string.IsNullOrEmpty(_apiBaseUrl))
                throw new InvalidOperationException("Сервис не авторизован");

            var getGroupsRequest = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "IpAddressGroups.get",
                @params = new
                {
                    query = new
                    {
                        orderBy = new[] {
                    new { columnName = "groupId", direction = "Asc" }
                }
                    },
                    refresh = true
                }
            };

            var requestJson = JsonConvert.SerializeObject(getGroupsRequest);

            var httpReq = new HttpRequestMessage(HttpMethod.Post, _apiBaseUrl);
            httpReq.Headers.Add("X-Token", _authToken);
            httpReq.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(httpReq);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var groupsData = JsonConvert.DeserializeObject<dynamic>(content);

            var result = new List<string>();
            if (groupsData?.result?.list != null)
            {
                var uniqueGroupNames = new HashSet<string>();
                foreach (var group in groupsData.result.list)
                {
                    string groupName = (string)group.groupName;
                    if (uniqueGroupNames.Add(groupName))
                        result.Add(groupName);
                }
            }

            return result;
        }

        public async Task<List<string>> GetUrlGroupsAsync()
        {
            if (string.IsNullOrEmpty(_authToken) || string.IsNullOrEmpty(_apiBaseUrl))
                throw new InvalidOperationException("Сервис не авторизован");

            var getGroupsRequest = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "UrlGroups.get",
                @params = new
                {
                    query = new
                    {
                        orderBy = new[] {
                    new { columnName = "groupId", direction = "Asc" }
                }
                    }
                }
            };

            var requestJson = JsonConvert.SerializeObject(getGroupsRequest);

            var httpReq = new HttpRequestMessage(HttpMethod.Post, _apiBaseUrl);
            httpReq.Headers.Add("X-Token", _authToken);
            httpReq.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(httpReq);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var groupsData = JsonConvert.DeserializeObject<dynamic>(content);

            var result = new List<string>();
            if (groupsData?.result?.list != null)
            {
                var uniqueGroupNames = new HashSet<string>();
                foreach (var group in groupsData.result.list)
                {
                    string groupName = (string)group.groupName;
                    if (uniqueGroupNames.Add(groupName))
                        result.Add(groupName);
                }
            }

            return result;
        }

        public async Task<List<GroupModel>> GetFullIpGroupsAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_authToken) || string.IsNullOrEmpty(_apiBaseUrl))
                    throw new InvalidOperationException("Сервис не авторизован");

                var getGroupsRequest = new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "IpAddressGroups.get",
                    @params = new
                    {
                        query = new
                        {
                            orderBy = new[] { new { columnName = "groupName", direction = "Asc" } }
                        },
                        refresh = true
                    }
                };

                var requestJson = JsonConvert.SerializeObject(getGroupsRequest);
                var httpReq = new HttpRequestMessage(HttpMethod.Post, _apiBaseUrl);
                httpReq.Headers.Add("X-Token", _authToken);
                httpReq.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                Console.WriteLine($"📤 Запрос IP групп: {requestJson}");

                var response = await _httpClient.SendAsync(httpReq);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ HTTP ошибка: {response.StatusCode}");
                    return new List<GroupModel>();
                }

                var content = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"📥 Полный ответ IP групп: {content}");

                var groupsData = JsonConvert.DeserializeObject<dynamic>(content);

                // Проверяем структуру ответа
                if (groupsData?.error != null)
                {
                    Console.WriteLine($"❌ Ошибка API: {groupsData.error}");
                    return new List<GroupModel>();
                }

                if (groupsData?.result == null)
                {
                    Console.WriteLine("❌ Ответ не содержит result");
                    return new List<GroupModel>();
                }

                Console.WriteLine($"🔍 Структура result: {JsonConvert.SerializeObject(groupsData.result)}");

                var result = new List<GroupModel>();

                if (groupsData.result.list != null)
                {
                    Console.WriteLine($"🔍 Найдено записей: {groupsData.result.list.Count}");

                    var groupedRecords = new Dictionary<string, GroupModel>();

                    foreach (var record in groupsData.result.list)
                    {
                        try
                        {
                            string groupName = record.groupName?.ToString() ?? "Unknown";
                            string host = record.host?.ToString() ?? "";
                            string description = record.description?.ToString() ?? "";

                            Console.WriteLine($"   📄 Запись: groupName='{groupName}', host='{host}'");

                            if (!groupedRecords.ContainsKey(groupName))
                            {
                                groupedRecords[groupName] = new GroupModel
                                {
                                    GroupName = groupName,
                                    Description = description,
                                    Items = new List<string>()
                                };
                            }

                            if (!string.IsNullOrEmpty(host))
                            {
                                groupedRecords[groupName].Items.Add(host);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ Ошибка обработки записи: {ex.Message}");
                        }
                    }

                    result = groupedRecords.Values.ToList();
                    Console.WriteLine($"📊 Сформировано групп: {result.Count}");
                }
                else
                {
                    Console.WriteLine("ℹ️ list is null в ответе");
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка в GetFullIpGroupsAsync: {ex.Message}");
                return new List<GroupModel>();
            }
        }

        public async Task<List<GroupModel>> GetFullUrlGroupsAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_authToken) || string.IsNullOrEmpty(_apiBaseUrl))
                    throw new InvalidOperationException("Сервис не авторизован");

                var getGroupsRequest = new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "UrlGroups.get",
                    @params = new
                    {
                        query = new
                        {
                            orderBy = new[] { new { columnName = "groupName", direction = "Asc" } }
                        },
                        refresh = true
                    }
                };

                var requestJson = JsonConvert.SerializeObject(getGroupsRequest);
                var httpReq = new HttpRequestMessage(HttpMethod.Post, _apiBaseUrl);
                httpReq.Headers.Add("X-Token", _authToken);
                httpReq.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                Console.WriteLine($"📤 Запрос URL групп: {requestJson}");

                var response = await _httpClient.SendAsync(httpReq);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ HTTP ошибка: {response.StatusCode}");
                    return new List<GroupModel>();
                }

                var content = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"📥 Полный ответ URL групп: {content}");

                var groupsData = JsonConvert.DeserializeObject<dynamic>(content);

                // Проверяем структуру ответа
                if (groupsData?.error != null)
                {
                    Console.WriteLine($"❌ Ошибка API: {groupsData.error}");
                    return new List<GroupModel>();
                }

                if (groupsData?.result == null)
                {
                    Console.WriteLine("❌ Ответ не содержит result");
                    return new List<GroupModel>();
                }

                Console.WriteLine($"🔍 Структура result: {JsonConvert.SerializeObject(groupsData.result)}");

                var result = new List<GroupModel>();

                if (groupsData.result.list != null)
                {
                    Console.WriteLine($"🔍 Найдено записей: {groupsData.result.list.Count}");

                    var groupedRecords = new Dictionary<string, GroupModel>();

                    foreach (var record in groupsData.result.list)
                    {
                        try
                        {
                            string groupName = record.groupName?.ToString() ?? "Unknown";
                            string url = record.url?.ToString() ?? ""; // Для URL групп используем поле url
                            string description = record.description?.ToString() ?? "";

                            Console.WriteLine($"   📄 Запись: groupName='{groupName}', url='{url}'");

                            if (!groupedRecords.ContainsKey(groupName))
                            {
                                groupedRecords[groupName] = new GroupModel
                                {
                                    GroupName = groupName,
                                    Description = description,
                                    Items = new List<string>()
                                };
                            }

                            if (!string.IsNullOrEmpty(url))
                            {
                                groupedRecords[groupName].Items.Add(url);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ Ошибка обработки записи: {ex.Message}");
                        }
                    }

                    result = groupedRecords.Values.ToList();
                    Console.WriteLine($"📊 Сформировано URL групп: {result.Count}");

                    foreach (var group in result)
                    {
                        Console.WriteLine($"   📝 Группа: '{group.GroupName}', URL элементов: {group.Items.Count}");
                    }
                }
                else
                {
                    Console.WriteLine("ℹ️ list is null в ответе URL групп");
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка в GetFullUrlGroupsAsync: {ex.Message}");
                return new List<GroupModel>();
            }
        }

        public Task<string> GetAntivirusStatusAsync()
        {
            return Task.FromResult("Статус антивируса: Недоступно");
        }

        public Task<bool> SetAntivirusStatusAsync(bool enable)
        {
            return Task.FromResult(true);
        }

        public Task ExportGroupsToCsvAsync(string exportType)
        {
            return Task.CompletedTask;
        }

        // Добавьте этот метод в KerioApiService.cs
        public async Task<List<FullGroupData>> GetDetailedIpGroupsAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_authToken) || string.IsNullOrEmpty(_apiBaseUrl))
                    throw new InvalidOperationException("Сервис не авторизован");

                var getGroupsRequest = new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "IpAddressGroups.get",
                    @params = new
                    {
                        query = new
                        {
                            orderBy = new[] { new { columnName = "groupName", direction = "Asc" } }
                        },
                        refresh = true
                    }
                };

                var requestJson = JsonConvert.SerializeObject(getGroupsRequest);
                var httpReq = new HttpRequestMessage(HttpMethod.Post, _apiBaseUrl);
                httpReq.Headers.Add("X-Token", _authToken);
                httpReq.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                Console.WriteLine($"📤 Запрос детальных IP групп: {requestJson}");

                var response = await _httpClient.SendAsync(httpReq);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ HTTP ошибка: {response.StatusCode}");
                    return new List<FullGroupData>();
                }

                var content = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"📥 Полный ответ детальных IP групп: {content}");

                var groupsData = JsonConvert.DeserializeObject<dynamic>(content);

                if (groupsData?.error != null)
                {
                    Console.WriteLine($"❌ Ошибка API: {groupsData.error}");
                    return new List<FullGroupData>();
                }

                if (groupsData?.result == null)
                {
                    Console.WriteLine("❌ Ответ не содержит result");
                    return new List<FullGroupData>();
                }

                var result = new List<FullGroupData>();

                if (groupsData.result.list != null)
                {
                    Console.WriteLine($"🔍 Найдено записей: {groupsData.result.list.Count}");

                    var groupedRecords = new Dictionary<string, FullGroupData>();

                    foreach (var record in groupsData.result.list)
                    {
                        try
                        {
                            string groupName = record.groupName?.ToString() ?? "Unknown";
                            string host = record.host?.ToString() ?? "";
                            string description = record.description?.ToString() ?? "";

                            Console.WriteLine($"   📄 Запись: groupName='{groupName}', host='{host}', description='{description}'");

                            if (!groupedRecords.ContainsKey(groupName))
                            {
                                groupedRecords[groupName] = new FullGroupData
                                {
                                    GroupName = groupName,
                                    Items = new List<GroupItemDetail>()
                                };
                            }

                            if (!string.IsNullOrEmpty(host))
                            {
                                groupedRecords[groupName].Items.Add(new GroupItemDetail
                                {
                                    Host = host,
                                    Description = description
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ Ошибка обработки записи: {ex.Message}");
                        }
                    }

                    result = groupedRecords.Values.ToList();
                    Console.WriteLine($"📊 Сформировано детальных групп: {result.Count}");
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка в GetDetailedIpGroupsAsync: {ex.Message}");
                return new List<FullGroupData>();
            }
        }

        // Аналогично для URL групп
        public async Task<List<FullGroupData>> GetDetailedUrlGroupsAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_authToken) || string.IsNullOrEmpty(_apiBaseUrl))
                    throw new InvalidOperationException("Сервис не авторизован");

                var getGroupsRequest = new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "UrlGroups.get",
                    @params = new
                    {
                        query = new
                        {
                            orderBy = new[] { new { columnName = "groupName", direction = "Asc" } }
                        },
                        refresh = true
                    }
                };

                var requestJson = JsonConvert.SerializeObject(getGroupsRequest);
                var httpReq = new HttpRequestMessage(HttpMethod.Post, _apiBaseUrl);
                httpReq.Headers.Add("X-Token", _authToken);
                httpReq.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");

                Console.WriteLine($"📤 Запрос детальных URL групп: {requestJson}");

                var response = await _httpClient.SendAsync(httpReq);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"❌ HTTP ошибка: {response.StatusCode}");
                    return new List<FullGroupData>();
                }

                var content = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"📥 Полный ответ детальных URL групп: {content}");

                var groupsData = JsonConvert.DeserializeObject<dynamic>(content);

                if (groupsData?.error != null)
                {
                    Console.WriteLine($"❌ Ошибка API: {groupsData.error}");
                    return new List<FullGroupData>();
                }

                if (groupsData?.result == null)
                {
                    Console.WriteLine("❌ Ответ не содержит result");
                    return new List<FullGroupData>();
                }

                var result = new List<FullGroupData>();

                if (groupsData.result.list != null)
                {
                    Console.WriteLine($"🔍 Найдено записей: {groupsData.result.list.Count}");

                    var groupedRecords = new Dictionary<string, FullGroupData>();

                    foreach (var record in groupsData.result.list)
                    {
                        try
                        {
                            string groupName = record.groupName?.ToString() ?? "Unknown";
                            string url = record.url?.ToString() ?? "";
                            string description = record.description?.ToString() ?? "";

                            Console.WriteLine($"   📄 Запись: groupName='{groupName}', url='{url}', description='{description}'");

                            if (!groupedRecords.ContainsKey(groupName))
                            {
                                groupedRecords[groupName] = new FullGroupData
                                {
                                    GroupName = groupName,
                                    Items = new List<GroupItemDetail>()
                                };
                            }

                            if (!string.IsNullOrEmpty(url))
                            {
                                groupedRecords[groupName].Items.Add(new GroupItemDetail
                                {
                                    Host = url, // Для URL групп используем Host поле для хранения URL
                                    Description = description
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ Ошибка обработки записи: {ex.Message}");
                        }
                    }

                    result = groupedRecords.Values.ToList();
                    Console.WriteLine($"📊 Сформировано детальных URL групп: {result.Count}");
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Ошибка в GetDetailedUrlGroupsAsync: {ex.Message}");
                return new List<FullGroupData>();
            }
        }


    }
}
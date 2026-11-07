using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AntigravityGateway.Models;

namespace AntigravityGateway.Services
{
    public class DiagnosticResult
    {
        public bool TunnelPortOk { get; set; }
        public string ExternalIp { get; set; } = "Не определен";
        public string Country { get; set; } = "Не определена";
        public string Org { get; set; } = "";
        public bool GeminiApiOk { get; set; }
        public string GeminiApiStatus { get; set; } = "Не проверен";
        public string AntigravityProcessStatus { get; set; } = "Не запущен";
        public string LanguageServerStatus { get; set; } = "Не запущен";
        public bool IsReadyForGemini { get; set; }
        public string SummaryMessage { get; set; } = "";
    }

    public class DiagnosticsService
    {
        private readonly ConfigModel _config;

        public DiagnosticsService(ConfigModel config)
        {
            _config = config;
        }

        public async Task<DiagnosticResult> RunFullDiagnosticsAsync()
        {
            var res = new DiagnosticResult();

            // 1. Check local proxy port
            res.TunnelPortOk = await TunnelService.CheckPortOpenAsync(_config.LocalPort, 1200);

            // 2. Check external IP and location through proxy
            if (res.TunnelPortOk)
            {
                try
                {
                    var handler = new HttpClientHandler
                    {
                        Proxy = new WebProxy($"http://127.0.0.1:{_config.LocalPort}"),
                        UseProxy = true
                    };

                    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
                    var response = await client.GetStringAsync("https://ipinfo.io/json");
                    using var doc = JsonDocument.Parse(response);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("ip", out var ipProp)) res.ExternalIp = ipProp.GetString() ?? "";
                    if (root.TryGetProperty("country", out var countryProp)) res.Country = countryProp.GetString() ?? "";
                    if (root.TryGetProperty("city", out var cityProp)) res.Country += $" ({cityProp.GetString()})";
                    if (root.TryGetProperty("org", out var orgProp)) res.Org = orgProp.GetString() ?? "";
                }
                catch (Exception ex)
                {
                    res.ExternalIp = $"Ошибка запроса через прокси: {ex.Message}";
                }
            }
            else
            {
                res.ExternalIp = "Туннель выключен";
            }

            // 3. Check Google Gemini CloudCode endpoint reachability
            if (res.TunnelPortOk)
            {
                try
                {
                    var handler = new HttpClientHandler
                    {
                        Proxy = new WebProxy($"http://127.0.0.1:{_config.LocalPort}"),
                        UseProxy = true
                    };

                    using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(7) };
                    using var req = new HttpRequestMessage(HttpMethod.Get, "https://cloudcode-pa.googleapis.com/");
                    var gResp = await client.SendAsync(req);

                    // 404/403/200 from Google means TCP + TLS handshake succeeded and endpoint is reachable
                    int code = (int)gResp.StatusCode;
                    if (code == 200 || code == 404 || code == 403 || code == 401)
                    {
                        res.GeminiApiOk = true;
                        res.GeminiApiStatus = $"Доступен (Код ответа Google: {code})";
                    }
                    else
                    {
                        res.GeminiApiOk = false;
                        res.GeminiApiStatus = $"Код {code}: {gResp.ReasonPhrase}";
                    }
                }
                catch (Exception ex)
                {
                    res.GeminiApiOk = false;
                    res.GeminiApiStatus = $"Таймаут или ошибка шлюза: {ex.Message}";
                }
            }
            else
            {
                res.GeminiApiStatus = "Недоступен (туннель не поднят)";
            }

            // 4. Check Antigravity and LanguageServer processes
            var procs = ProcessMonitorService.GetActiveStatus();
            res.AntigravityProcessStatus = procs.AntigravityRunning ? $"Работает (PID: {procs.AntigravityPid})" : "Не запущен";
            res.LanguageServerStatus = procs.LanguageServerRunning ? $"Работает (PID: {procs.LanguageServerPid})" : "Не запущен";

            // Determine readiness
            if (res.TunnelPortOk && res.GeminiApiOk)
            {
                res.IsReadyForGemini = true;
                res.SummaryMessage = "ВСЁ ГОТОВО: Соединение с серверами Gemini в порядке. Можно запускать Antigravity.";
            }
            else
            {
                res.IsReadyForGemini = false;
                res.SummaryMessage = "ВНИМАНИЕ: Канал до Google Gemini не подтвержден. Запустите туннель или смените режим обхода.";
            }

            return res;
        }
    }
}

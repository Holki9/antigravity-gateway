using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using AntigravityGateway.Models;

namespace AntigravityGateway.Services
{
    public class ProcessStatusInfo
    {
        public bool AntigravityRunning { get; set; }
        public int AntigravityPid { get; set; }
        public bool LanguageServerRunning { get; set; }
        public int LanguageServerPid { get; set; }
        public string LanguageServerArgs { get; set; } = "";
        public bool SshRunning { get; set; }
        public bool XrayRunning { get; set; }
    }

    public static class ProcessMonitorService
    {
        public static ProcessStatusInfo GetActiveStatus()
        {
            var info = new ProcessStatusInfo();

            var agProcs = Process.GetProcessesByName("Antigravity");
            if (agProcs.Length > 0)
            {
                info.AntigravityRunning = true;
                info.AntigravityPid = agProcs[0].Id;
            }

            var lsProcs = Process.GetProcessesByName("language_server");
            if (lsProcs.Length > 0)
            {
                info.LanguageServerRunning = true;
                info.LanguageServerPid = lsProcs[0].Id;
                info.LanguageServerArgs = GetCommandLine(lsProcs[0].Id);
            }

            info.SshRunning = Process.GetProcessesByName("ssh").Length > 0;
            info.XrayRunning = Process.GetProcessesByName("xray").Length > 0;

            return info;
        }

        public static void KillStuckLanguageServers()
        {
            var lsProcs = Process.GetProcessesByName("language_server");
            foreach (var p in lsProcs)
            {
                try { p.Kill(); } catch { }
            }
        }

        public static string GetCommandLine(int pid)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
                foreach (ManagementObject @object in searcher.Get())
                {
                    return @object["CommandLine"]?.ToString() ?? "";
                }
            }
            catch { }
            return "";
        }
    }

    public class LauncherService
    {
        private readonly ConfigModel _config;

        public LauncherService(ConfigModel config)
        {
            _config = config;
        }

        public bool LaunchAntigravity()
        {
            string exePath = _config.ResolvePath(_config.AntigravityExePath);
            if (!File.Exists(exePath))
            {
                // Try alternate locations
                string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string alt1 = Path.Combine(localApp, "Programs", "antigravity", "Antigravity.exe");
                string alt2 = Path.Combine(localApp, "Antigravity Client", "antigravity-client.exe");

                if (File.Exists(alt1)) exePath = alt1;
                else if (File.Exists(alt2)) exePath = alt2;
                else return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
                };

                // Inject proxy variables directly into the Antigravity process tree
                int proxyPort = _config.Mode == BypassMode.XrayVless ? _config.XrayLocalPort : _config.LocalPort;
                string proxyUrl = $"http://127.0.0.1:{proxyPort}";

                psi.EnvironmentVariables["HTTP_PROXY"] = proxyUrl;
                psi.EnvironmentVariables["HTTPS_PROXY"] = proxyUrl;
                psi.EnvironmentVariables["ALL_PROXY"] = proxyUrl;
                psi.EnvironmentVariables["NO_PROXY"] = "localhost,127.0.0.1,::1";
                psi.EnvironmentVariables["TZ"] = "Europe/Warsaw";
                psi.EnvironmentVariables["NODE_TLS_REJECT_UNAUTHORIZED"] = "0";

                if (!string.IsNullOrWhiteSpace(_config.GeminiApiKey))
                {
                    psi.EnvironmentVariables["GEMINI_API_KEY"] = _config.GeminiApiKey.Trim();
                }

                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> RestartLanguageServerWithGeminiAsync()
        {
            var status = ProcessMonitorService.GetActiveStatus();
            if (!status.LanguageServerRunning) return false;

            string cmdLine = status.LanguageServerArgs;
            int oldPid = status.LanguageServerPid;

            string lsExe = _config.ResolvePath(_config.LanguageServerPath);
            if (!File.Exists(lsExe))
            {
                // If not found in default path, derive from command line
                if (!string.IsNullOrEmpty(cmdLine))
                {
                    int firstQuote = cmdLine.IndexOf('\"');
                    int secondQuote = firstQuote >= 0 ? cmdLine.IndexOf('\"', firstQuote + 1) : -1;
                    if (firstQuote >= 0 && secondQuote > firstQuote)
                    {
                        lsExe = cmdLine.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                    }
                }
            }

            if (!File.Exists(lsExe)) return false;

            // Remove exe from args
            string args = cmdLine;
            if (args.StartsWith($"\"{lsExe}\"")) args = args.Substring(lsExe.Length + 2).Trim();
            else if (args.StartsWith(lsExe)) args = args.Substring(lsExe.Length).Trim();

            // Replace or add -model_api_client_type=gemini
            if (args.Contains("-model_api_client_type="))
            {
                args = System.Text.RegularExpressions.Regex.Replace(args, @"-model_api_client_type=\S+", "-model_api_client_type=gemini");
            }
            else
            {
                args += " -model_api_client_type=gemini";
            }

            // Terminate old process
            try
            {
                var p = Process.GetProcessById(oldPid);
                p.Kill();
                await Task.Delay(800);
            }
            catch { }

            // Relaunch with enforced proxy and timezone
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = lsExe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                int proxyPort = _config.Mode == BypassMode.XrayVless ? _config.XrayLocalPort : _config.LocalPort;
                string proxyUrl = $"http://127.0.0.1:{proxyPort}";

                psi.EnvironmentVariables["HTTP_PROXY"] = proxyUrl;
                psi.EnvironmentVariables["HTTPS_PROXY"] = proxyUrl;
                psi.EnvironmentVariables["ALL_PROXY"] = proxyUrl;
                psi.EnvironmentVariables["NO_PROXY"] = "localhost,127.0.0.1,::1";
                psi.EnvironmentVariables["TZ"] = "Europe/Warsaw";

                if (!string.IsNullOrWhiteSpace(_config.GeminiApiKey))
                {
                    psi.EnvironmentVariables["GEMINI_API_KEY"] = _config.GeminiApiKey.Trim();
                }

                Process.Start(psi);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

using System;
using System.IO;
using System.Text.Json;

namespace AntigravityGateway.Models
{
    public enum BypassMode
    {
        WarsawSshWarp = 0,     // SSH Local Forward to Warsaw + Cloudflare WARP
        XrayVless = 1,          // Direct Xray VLESS Reality / Proxy
        ProcessHook = 2,        // Specific language_server.exe injection
        DirectEnvOnly = 3       // Global / process environment injection only
    }

    public class ConfigModel
    {
        public BypassMode Mode { get; set; } = BypassMode.WarsawSshWarp;

        // Warsaw SSH Tunnel
        public string WarsawHost { get; set; } = "91.196.161.40";
        public int WarsawPort { get; set; } = 8585;
        public int LocalPort { get; set; } = 10810;
        public string SshUser { get; set; } = "root";
        public string SshKeyPath { get; set; } = @"%USERPROFILE%\.ssh\id_ed25519";

        // Xray Settings
        public string XrayExePath { get; set; } = @"C:\Program Files\FlyFrogLLC\Happ\core\xray.exe";
        public string XrayConfigPath { get; set; } = "";
        public int XrayLocalPort { get; set; } = 10808;

        // Antigravity Executable
        public string AntigravityExePath { get; set; } = @"%LOCALAPPDATA%\Programs\antigravity\Antigravity.exe";
        public string LanguageServerPath { get; set; } = @"%LOCALAPPDATA%\Programs\antigravity\resources\bin\language_server.exe";

        // Behavioral & Gemini fixes
        public bool ForceGeminiClientType { get; set; } = true;
        public string GeminiApiKey { get; set; } = "";
        public bool BlockIpv6Leak { get; set; } = true;
        public bool AutoStartTunnelOnLaunch { get; set; } = true;
        public bool MinimizeToTrayOnClose { get; set; } = true;

        public static string GetConfigFilePath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "AntigravityGateway");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "config.json");
        }

        public static ConfigModel Load()
        {
            try
            {
                string path = GetConfigFilePath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var cfg = JsonSerializer.Deserialize<ConfigModel>(json);
                    if (cfg != null) return cfg;
                }
            }
            catch { }
            return new ConfigModel();
        }

        public void Save()
        {
            try
            {
                string path = GetConfigFilePath();
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { }
        }

        public string ResolvePath(string pathWithEnv)
        {
            if (string.IsNullOrWhiteSpace(pathWithEnv)) return "";
            return Environment.ExpandEnvironmentVariables(pathWithEnv);
        }
    }
}

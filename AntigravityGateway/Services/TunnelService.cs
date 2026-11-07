using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AntigravityGateway.Models;

namespace AntigravityGateway.Services
{
    public class TunnelService
    {
        private Process? _sshProcess;
        private CancellationTokenSource? _monitorCts;
        private readonly ConfigModel _config;

        public event Action<bool, string>? StatusChanged;
        public bool IsRunning { get; private set; }

        public TunnelService(ConfigModel config)
        {
            _config = config;
        }

        public async Task<bool> StartTunnelAsync()
        {
            if (IsRunning && await CheckPortOpenAsync(_config.LocalPort))
            {
                StatusChanged?.Invoke(true, $"Туннель уже активен (порт {_config.LocalPort})");
                return true;
            }

            StopTunnel();

            string sshExe = FindSshExecutable();
            string keyPath = _config.ResolvePath(_config.SshKeyPath);

            string args = $"-N -o BatchMode=yes -o StrictHostKeyChecking=no -o ServerAliveInterval=10 -o ServerAliveCountMax=3 -o ExitOnForwardFailure=yes -o TCPKeepAlive=yes -i \"{keyPath}\" -L {_config.LocalPort}:127.0.0.1:{_config.WarsawPort} {_config.SshUser}@{_config.WarsawHost}";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = sshExe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                _sshProcess = Process.Start(psi);
                if (_sshProcess == null)
                {
                    StatusChanged?.Invoke(false, "Не удалось запустить ssh.exe");
                    return false;
                }

                // Wait up to 5 seconds for port to open
                for (int i = 0; i < 15; i++)
                {
                    await Task.Delay(400);
                    if (_sshProcess.HasExited)
                    {
                        string err = await _sshProcess.StandardError.ReadToEndAsync();
                        StatusChanged?.Invoke(false, $"SSH завершился с ошибкой: {err.Trim()}");
                        return false;
                    }

                    if (await CheckPortOpenAsync(_config.LocalPort))
                    {
                        IsRunning = true;
                        StatusChanged?.Invoke(true, $"Туннель подключен к {_config.WarsawHost} (порт {_config.LocalPort})");
                        StartHealthMonitor();
                        return true;
                    }
                }

                StatusChanged?.Invoke(false, "Таймаут открытия локального порта 10810");
                StopTunnel();
                return false;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(false, $"Ошибка запуска туннеля: {ex.Message}");
                return false;
            }
        }

        public void StopTunnel()
        {
            _monitorCts?.Cancel();
            _monitorCts = null;

            try
            {
                if (_sshProcess != null && !_sshProcess.HasExited)
                {
                    _sshProcess.Kill(true);
                    _sshProcess.Dispose();
                }
            }
            catch { }
            _sshProcess = null;

            // Kill any lingering orphan ssh tunnels targeting our local port
            KillOrphanSshProcesses();

            IsRunning = false;
            StatusChanged?.Invoke(false, "Туннель остановлен");
        }

        private void StartHealthMonitor()
        {
            _monitorCts = new CancellationTokenSource();
            var token = _monitorCts.Token;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(3000, token);
                    if (token.IsCancellationRequested) break;

                    bool ok = await CheckPortOpenAsync(_config.LocalPort);
                    if (!ok)
                    {
                        IsRunning = false;
                        StatusChanged?.Invoke(false, "Обрыв связи с Варшавой. Авто-переподключение...");
                        await StartTunnelAsync();
                        break;
                    }
                }
            }, token);
        }

        public static async Task<bool> CheckPortOpenAsync(int port, int timeoutMs = 800)
        {
            try
            {
                using var client = new TcpClient();
                using var cts = new CancellationTokenSource(timeoutMs);
                await client.ConnectAsync("127.0.0.1", port, cts.Token);
                return client.Connected;
            }
            catch
            {
                return false;
            }
        }

        private void KillOrphanSshProcesses()
        {
            try
            {
                var procs = Process.GetProcessesByName("ssh");
                foreach (var p in procs)
                {
                    try
                    {
                        // In case it's lingering tunnel
                        p.Kill();
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static string FindSshExecutable()
        {
            string winSsh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "OpenSSH", "ssh.exe");
            if (File.Exists(winSsh)) return winSsh;

            string progSsh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenSSH", "ssh.exe");
            if (File.Exists(progSsh)) return progSsh;

            return "ssh.exe";
        }
    }
}

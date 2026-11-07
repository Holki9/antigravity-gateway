using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AntigravityGateway.Models;
using AntigravityGateway.Services;
using WinForms = System.Windows.Forms;

namespace AntigravityGateway.Views
{
    public partial class MainWindow : Window
    {
        private readonly ConfigModel _config;
        private readonly TunnelService _tunnelService;
        private readonly DiagnosticsService _diagService;
        private readonly LauncherService _launcherService;
        private WinForms.NotifyIcon? _notifyIcon;

        public MainWindow()
        {
            InitializeComponent();

            _config = ConfigModel.Load();
            _tunnelService = new TunnelService(_config);
            _diagService = new DiagnosticsService(_config);
            _launcherService = new LauncherService(_config);

            _tunnelService.StatusChanged += OnTunnelStatusChanged;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            SetupTrayIcon();
            LoadConfigToUi();

            if (_config.AutoStartTunnelOnLaunch)
            {
                await _tunnelService.StartTunnelAsync();
            }

            // Quick background diagnostic
            _ = RunDiagnosticsSilentAsync();
        }

        private void SetupTrayIcon()
        {
            try
            {
                _notifyIcon = new WinForms.NotifyIcon
                {
                    Icon = SystemIcons.Shield,
                    Visible = true,
                    Text = "Antigravity Gateway Pro"
                };

                var contextMenu = new WinForms.ContextMenuStrip();
                contextMenu.Items.Add("Открыть окно", null, (s, e) => ShowAndRestoreWindow());
                contextMenu.Items.Add("Сбросить Language Server (Gemini)", null, async (s, e) =>
                {
                    await _launcherService.RestartLanguageServerWithGeminiAsync();
                });
                contextMenu.Items.Add("-");
                contextMenu.Items.Add("Выход", null, (s, e) =>
                {
                    _config.MinimizeToTrayOnClose = false;
                    Close();
                });

                _notifyIcon.ContextMenuStrip = contextMenu;
                _notifyIcon.DoubleClick += (s, e) => ShowAndRestoreWindow();
            }
            catch { }
        }

        private void ShowAndRestoreWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_config.MinimizeToTrayOnClose)
            {
                e.Cancel = true;
                Hide();
                _notifyIcon?.ShowBalloonTip(2000, "Antigravity Gateway", "Приложение свернуто в трей и защищает соединение Gemini.", WinForms.ToolTipIcon.Info);
            }
            else
            {
                _tunnelService.StopTunnel();
                _notifyIcon?.Dispose();
                _notifyIcon = null;
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (ViewModes == null || ViewDiagnostics == null || ViewTools == null || ViewSettings == null) return;

            ViewModes.Visibility = Visibility.Collapsed;
            ViewDiagnostics.Visibility = Visibility.Collapsed;
            ViewTools.Visibility = Visibility.Collapsed;
            ViewSettings.Visibility = Visibility.Collapsed;

            if (TabModes.IsChecked == true) ViewModes.Visibility = Visibility.Visible;
            else if (TabDiagnostics.IsChecked == true)
            {
                ViewDiagnostics.Visibility = Visibility.Visible;
                _ = RunDiagnosticsSilentAsync();
            }
            else if (TabTools.IsChecked == true) ViewTools.Visibility = Visibility.Visible;
            else if (TabSettings.IsChecked == true) ViewSettings.Visibility = Visibility.Visible;
        }

        private void LoadConfigToUi()
        {
            InputWarsawHost.Text = _config.WarsawHost;
            InputWarsawPort.Text = _config.WarsawPort.ToString();
            InputLocalPort.Text = _config.LocalPort.ToString();
            InputSshKey.Text = _config.SshKeyPath;
            InputAgPath.Text = _config.AntigravityExePath;
            InputApiKey.Text = _config.GeminiApiKey;

            switch (_config.Mode)
            {
                case BypassMode.WarsawSshWarp: RbModeWarsawSsh.IsChecked = true; break;
                case BypassMode.XrayVless: RbModeXrayVless.IsChecked = true; break;
                case BypassMode.ProcessHook: RbModeProcessHook.IsChecked = true; break;
                case BypassMode.DirectEnvOnly: RbModeDirectEnv.IsChecked = true; break;
            }

            UpdateModeHeader();
        }

        private void RbMode_Checked(object sender, RoutedEventArgs e)
        {
            if (RbModeWarsawSsh.IsChecked == true) _config.Mode = BypassMode.WarsawSshWarp;
            else if (RbModeXrayVless.IsChecked == true) _config.Mode = BypassMode.XrayVless;
            else if (RbModeProcessHook.IsChecked == true) _config.Mode = BypassMode.ProcessHook;
            else if (RbModeDirectEnv.IsChecked == true) _config.Mode = BypassMode.DirectEnvOnly;

            UpdateModeHeader();
        }

        private void UpdateModeHeader()
        {
            if (TxtActiveModeName == null) return;

            switch (_config.Mode)
            {
                case BypassMode.WarsawSshWarp:
                    TxtActiveModeName.Text = "Режим: Варшава SSH + Cloudflare WARP";
                    TxtHeaderDetail.Text = $"Туннель слушает 127.0.0.1:{_config.LocalPort}. Трафик Gemini защищен от утечек IPv6.";
                    break;
                case BypassMode.XrayVless:
                    TxtActiveModeName.Text = "Режим: Xray VLESS Reality";
                    TxtHeaderDetail.Text = "Прямой обходной VLESS-канал. Порты: 10808 (SOCKS) / 10809 (HTTP).";
                    break;
                case BypassMode.ProcessHook:
                    TxtActiveModeName.Text = "Режим: Точечный перехват сокетов (Process Hook)";
                    TxtHeaderDetail.Text = "Проксируются только language_server.exe и Antigravity.exe.";
                    break;
                case BypassMode.DirectEnvOnly:
                    TxtActiveModeName.Text = "Режим: Инъекция переменных и API Key";
                    TxtHeaderDetail.Text = "Обход региона через GEMINI_API_KEY и подмену таймзоны Europe/Warsaw.";
                    break;
            }
        }

        private async void BtnToggleTunnel_Click(object sender, RoutedEventArgs e)
        {
            BtnToggleTunnel.IsEnabled = false;
            try
            {
                if (_tunnelService.IsRunning)
                {
                    _tunnelService.StopTunnel();
                }
                else
                {
                    await _tunnelService.StartTunnelAsync();
                }
            }
            finally
            {
                BtnToggleTunnel.IsEnabled = true;
            }
        }

        private void OnTunnelStatusChanged(bool isUp, string message)
        {
            Dispatcher.Invoke(() =>
            {
                if (isUp)
                {
                    StatusBadge.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 38, 56, 43));
                    StatusDot.Fill = (SolidColorBrush)FindResource("AccentGreenBrush");
                    TxtStatusBadge.Text = "КАНАЛ СВЯЗИ АКТИВЕН";
                    TxtStatusBadge.Foreground = (SolidColorBrush)FindResource("AccentGreenBrush");
                    BtnToggleTunnel.Content = "🛑 Остановить туннель";
                }
                else
                {
                    StatusBadge.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 56, 38, 43));
                    StatusDot.Fill = (SolidColorBrush)FindResource("AccentRedBrush");
                    TxtStatusBadge.Text = "ТУННЕЛЬ ВЫКЛЮЧЕН";
                    TxtStatusBadge.Foreground = (SolidColorBrush)FindResource("AccentRedBrush");
                    BtnToggleTunnel.Content = "🔌 Подключить туннель";
                }
                TxtFooterInfo.Text = message;
            });
        }

        private async void BtnApplyCurrentMode_Click(object sender, RoutedEventArgs e)
        {
            _config.Save();
            TxtFooterInfo.Text = "Режим обхода применен. Перезапуск сетевого шлюза...";
            _tunnelService.StopTunnel();
            await Task.Delay(400);
            await _tunnelService.StartTunnelAsync();
        }

        private async void BtnLaunchAntigravity_Click(object sender, RoutedEventArgs e)
        {
            BtnLaunchAntigravity.IsEnabled = false;
            try
            {
                if (!_tunnelService.IsRunning && _config.Mode == BypassMode.WarsawSshWarp)
                {
                    TxtFooterInfo.Text = "Запуск Варшавского туннеля перед стартом Antigravity...";
                    bool ok = await _tunnelService.StartTunnelAsync();
                    if (!ok)
                    {
                        System.Windows.MessageBox.Show("Не удалось запустить защитный туннель до Варшавы. Проверьте настройки SSH.", "Предупреждение", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    }
                }

                bool launched = _launcherService.LaunchAntigravity();
                if (launched)
                {
                    TxtFooterInfo.Text = "Antigravity успешно запущен с защитой Gemini!";
                }
                else
                {
                    System.Windows.MessageBox.Show("Не удалось найти Antigravity.exe по указанному пути. Проверьте вкладку «Настройки».", "Файл не найден", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
            finally
            {
                BtnLaunchAntigravity.IsEnabled = true;
            }
        }

        private async void BtnQuickRestartLs_Click(object sender, RoutedEventArgs e)
        {
            TxtFooterInfo.Text = "Сброс и форсирование Gemini для Language Server...";
            bool restarted = await _launcherService.RestartLanguageServerWithGeminiAsync();
            if (restarted)
            {
                TxtFooterInfo.Text = "Language Server перезапущен с параметром -model_api_client_type=gemini!";
            }
            else
            {
                TxtFooterInfo.Text = "Language Server не обнаружен (запустите сначала Antigravity).";
            }
        }

        private void BtnKillAll_Click(object sender, RoutedEventArgs e)
        {
            ProcessMonitorService.KillStuckLanguageServers();
            _tunnelService.StopTunnel();
            TxtFooterInfo.Text = "Все фоновые процессы и туннели принудительно остановлены.";
        }

        private async void BtnRunDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            await RunDiagnosticsSilentAsync();
        }

        private async Task RunDiagnosticsSilentAsync()
        {
            TxtDiagTunnelPort.Text = "Тестирование...";
            TxtDiagIp.Text = "Определение...";
            TxtDiagGeminiApi.Text = "Проверка...";
            TxtDiagLsProcess.Text = "Опрос...";

            var res = await _diagService.RunFullDiagnosticsAsync();

            TxtDiagTunnelPort.Text = res.TunnelPortOk ? $"Открыт (порт {_config.LocalPort}) [OK]" : "Закрыт / Выключен";
            TxtDiagTunnelPort.Foreground = res.TunnelPortOk ? (SolidColorBrush)FindResource("AccentGreenBrush") : (SolidColorBrush)FindResource("AccentRedBrush");

            TxtDiagIp.Text = res.ExternalIp;
            TxtDiagCountry.Text = string.IsNullOrEmpty(res.Country) ? "" : $"{res.Country} • {res.Org}";

            TxtDiagGeminiApi.Text = res.GeminiApiOk ? "Связь установлена (OK)" : res.GeminiApiStatus;
            TxtDiagGeminiApi.Foreground = res.GeminiApiOk ? (SolidColorBrush)FindResource("AccentGreenBrush") : (SolidColorBrush)FindResource("AccentRedBrush");

            TxtDiagLsProcess.Text = res.LanguageServerStatus;
            TxtDiagAgProcess.Text = $"Antigravity: {res.AntigravityProcessStatus}";

            TxtDiagSummary.Text = res.SummaryMessage;
            CardSummary.BorderBrush = res.IsReadyForGemini ? (SolidColorBrush)FindResource("AccentGreenBrush") : (SolidColorBrush)FindResource("AccentRedBrush");
        }

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            _config.WarsawHost = InputWarsawHost.Text.Trim();
            if (int.TryParse(InputWarsawPort.Text.Trim(), out int wp)) _config.WarsawPort = wp;
            if (int.TryParse(InputLocalPort.Text.Trim(), out int lp)) _config.LocalPort = lp;
            _config.SshKeyPath = InputSshKey.Text.Trim();
            _config.AntigravityExePath = InputAgPath.Text.Trim();
            _config.GeminiApiKey = InputApiKey.Text.Trim();

            _config.Save();
            TxtFooterInfo.Text = "Настройки успешно сохранены!";
        }
    }
}

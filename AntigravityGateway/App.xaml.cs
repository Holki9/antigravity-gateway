using System;
using System.Threading;
using System.Windows;
using AntigravityGateway.Views;

namespace AntigravityGateway
{
    public partial class App : System.Windows.Application
    {
        private static Mutex? _singleInstanceMutex;

        private void Application_Startup(object sender, System.Windows.StartupEventArgs e)
        {
            const string mutexName = "Global\\AntigravityGatewayPro_SingleInstanceMutex";
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                System.Windows.MessageBox.Show("Antigravity Gateway Pro уже запущен! Проверьте панель задач или системный трей.",
                                "Antigravity Gateway Pro", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                Shutdown();
                return;
            }

            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        private void Application_Exit(object sender, System.Windows.ExitEventArgs e)
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
    }
}

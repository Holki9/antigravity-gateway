using System;
using System.Threading;
using System.Windows;
using AntigravityGateway.Views;

namespace AntigravityGateway
{
    public partial class App : Application
    {
        private static Mutex? _singleInstanceMutex;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            const string mutexName = "Global\\AntigravityGatewayPro_SingleInstanceMutex";
            _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);

            if (!createdNew)
            {
                MessageBox.Show("Antigravity Gateway Pro уже запущен! Проверьте панель задач или системный трей.",
                                "Antigravity Gateway Pro", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            var mainWindow = new MainWindow();
            mainWindow.Show();
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
    }
}

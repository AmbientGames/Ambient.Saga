using Ambient.Saga.Sandbox.WindowsUI.Services;
using Ambient.Saga.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Ambient.Saga.Sandbox.WindowsUI
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            try
            {
                // To customize application configuration such as set high DPI settings or default font,
                // see https://aka.ms/applicationconfiguration.
                // IMPORTANT: Must be called before any WinForms controls are created
                ApplicationConfiguration.Initialize();

                // Enable dark mode
                System.Windows.Forms.Application.SetColorMode(SystemColorMode.Dark);

                // Initialize Steam (creates Timer control, so must be after ApplicationConfiguration)
                ServiceProviderSetup.InitializeSteam();

                // Set Steam status for Schema.Sandbox library
                SteamContext.IsSteamInitialized = ServiceProviderSetup.IsSteamInitialized;

                // Build dependency injection container
                var services = ServiceProviderSetup.BuildServiceProvider();

                // Create and run main window using DI
                MainWindow? mainWindow = null;
                try
                {
                    mainWindow = services.GetRequiredService<MainWindow>();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Failed to initialize graphics. Please ensure DirectX 11 is supported.\n\n{ex.Message}",
                        "Initialization Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                try
                {
                    System.Windows.Forms.Application.Run(mainWindow);
                }
                finally
                {
                    // Cleanup on exit - always runs even if exception occurs
                    ServiceProviderSetup.ShutdownSteam();
                    services.Dispose();
                }
            }
            catch (Exception ex)
            {
                // Ensure Steam is shut down even on startup failure
                ServiceProviderSetup.ShutdownSteam();

                MessageBox.Show(
                    $"Application startup failed: {ex.Message}\n\n{ex.StackTrace}",
                    "Startup Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
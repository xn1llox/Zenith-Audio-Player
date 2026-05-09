using Microsoft.UI.Xaml;
using System.Diagnostics;

namespace ZenithAudio;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += App_UnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            LogStartupException(ex);
            throw;
        }
    }

    private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogStartupException(e.Exception);
    }

    private static void LogStartupException(Exception ex)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "startup-error.log");
        File.WriteAllText(path, ex.ToString());
        Debug.WriteLine(ex);
    }
}

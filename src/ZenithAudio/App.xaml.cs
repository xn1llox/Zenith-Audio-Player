using Microsoft.UI.Xaml;
using System.Diagnostics;
using System.Runtime.InteropServices;

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
            var launchFilePath = GetLaunchFilePath(args.Arguments);
            var mainWindow = new MainWindow();
            _window = mainWindow;
            mainWindow.Activate();

            if (!string.IsNullOrWhiteSpace(launchFilePath))
            {
                mainWindow.DispatcherQueue.TryEnqueue(async () =>
                {
                    await mainWindow.OpenAndPlayFileAsync(launchFilePath);
                });
            }
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
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZenithAudio",
            "Logs");
        Directory.CreateDirectory(logDirectory);
        var path = Path.Combine(logDirectory, "startup-error.log");
        File.WriteAllText(path, ex.ToString());
        Debug.WriteLine(ex);
    }

    private static string? GetLaunchFilePath(string? launchArguments)
    {
        foreach (var argument in Environment.GetCommandLineArgs().Skip(1))
        {
            if (TryNormalizeLaunchFile(argument, out var filePath))
            {
                return filePath;
            }
        }

        foreach (var argument in SplitCommandLineArguments(launchArguments))
        {
            if (TryNormalizeLaunchFile(argument, out var filePath))
            {
                return filePath;
            }
        }

        return null;
    }

    private static bool TryNormalizeLaunchFile(string? value, out string filePath)
    {
        filePath = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim().Trim('"');
        if (candidate.StartsWith("file:///", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            candidate = uri.LocalPath;
        }

        if (!File.Exists(candidate))
        {
            return false;
        }

        filePath = Path.GetFullPath(candidate);
        return true;
    }

    private static IEnumerable<string> SplitCommandLineArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            yield break;
        }

        var argv = CommandLineToArgvW(arguments, out var argc);
        if (argv == IntPtr.Zero)
        {
            yield break;
        }

        try
        {
            for (var index = 0; index < argc; index++)
            {
                var argumentPointer = Marshal.ReadIntPtr(argv, index * IntPtr.Size);
                var argument = Marshal.PtrToStringUni(argumentPointer);
                if (!string.IsNullOrWhiteSpace(argument))
                {
                    yield return argument;
                }
            }
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine,
        out int pNumArgs);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);
}

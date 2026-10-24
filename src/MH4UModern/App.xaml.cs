using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using MH4UModern.Services;

namespace MH4UModern;

public partial class App : Application
{
    private static readonly object CrashLogLock = new();
    public static bool IsSmokeTest { get; private set; }
    public static string SmokeTestDirectory => Environment.GetEnvironmentVariable("CLASSIC_MH_TOOLS_SMOKE_DIR")
        ?? Path.Combine(AppContext.BaseDirectory, "smoke-data");

    protected override void OnStartup(StartupEventArgs e)
    {
        IsSmokeTest = e.Args.Contains("--smoke-test", StringComparer.Ordinal);
        // The upstream mh4edit core resolves lists/ and images/ relative to the process directory.
        Environment.CurrentDirectory = AppContext.BaseDirectory;

        // Repair the known mh4edit 1.0.2 waist-ID lookup before the core
        // constructs MonHunEquip.equipWaist. No save bytes are changed here.
        WaistArmorIdFixService.Apply();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);
    }

    private static string CrashLogPath => Path.Combine(IsSmokeTest ? SmokeTestDirectory : AppContext.BaseDirectory, "crash-log.txt");

    public static void LogException(Exception exception, string context)
    {
        try
        {
            lock (CrashLogLock)
            {
                var sb = new StringBuilder();
                sb.AppendLine("============================================================");
                sb.AppendLine($"MH4U Modern v{typeof(App).Assembly.GetName().Version} crash report");
                sb.AppendLine($"Time: {DateTimeOffset.Now:O}");
                sb.AppendLine($"Context: {context}");
                sb.AppendLine($"OS: {Environment.OSVersion}");
                sb.AppendLine($".NET: {Environment.Version}");
                sb.AppendLine();
                sb.AppendLine(exception.ToString());
                sb.AppendLine();
                File.AppendAllText(CrashLogPath, sb.ToString());
            }
        }
        catch
        {
            // Never let logging create a second failure.
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException(e.Exception, "WPF DispatcherUnhandledException");
        if (IsSmokeTest)
        {
            e.Handled = true;
            Shutdown(1);
            return;
        }
        try
        {
            MessageBox.Show(
                "MH4U Modern caught a UI error instead of closing.\n\n" +
                "A crash-log.txt file was written beside the app. Send that file to me so we can patch the exact cause.\n\n" +
                e.Exception.Message,
                "MH4U Modern - UI error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch { }

        // Most hover/selection failures are recoverable UI exceptions. Keep the test session alive
        // so the exact stack trace can be captured instead of the process disappearing instantly.
        e.Handled = true;
    }

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogException(ex, $"AppDomain.UnhandledException (terminating={e.IsTerminating})");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException(e.Exception, "TaskScheduler.UnobservedTaskException");
        e.SetObserved();
    }
}

using AramTrackerCompanion.Ui;

namespace AramTrackerCompanion;

internal static class Program
{
    private const string InstanceName = @"Local\ARAMTrackerCompanion.Instance";

    [STAThread]
    private static void Main(string[] arguments)
    {
        var link = arguments.FirstOrDefault(a => a.StartsWith(LinkProtocol.Scheme + ":", StringComparison.OrdinalIgnoreCase));

        using var instance = new Mutex(initiallyOwned: true, InstanceName, out var isFirstInstance);

        if (!isFirstInstance)
        {
            InstanceChannel.TrySend(link ?? InstanceChannel.ShowWindowMessage);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error("Unhandled interface error", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                Log.Error("Unhandled error", exception);
            }
        };

        var startMinimized = arguments.Contains(StartupService.MinimizedArgument, StringComparer.OrdinalIgnoreCase);
        Log.Info("Companion started", ("version", AppInfo.Version), ("minimized", startMinimized), ("fromLink", link is not null));

        using var channel = new InstanceChannel();
        using var application = new TrayApplication(startMinimized, channel, LinkProtocol.ReadPairingCode(link));
        Application.Run(application);

        Log.Info("Companion stopped");
    }
}

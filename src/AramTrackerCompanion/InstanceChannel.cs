using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace AramTrackerCompanion;

internal sealed class InstanceChannel : IDisposable
{
    public const string ShowWindowMessage = "show";

    private const string DefaultPipeName = "ARAMTrackerCompanion.Instance";
    private const int MaxMessageLength = 512;
    private const int AnyProcess = -1;

    private readonly CancellationTokenSource _cancellation = new();
    private readonly string _pipeName;

    public InstanceChannel(string pipeName = DefaultPipeName)
    {
        _pipeName = pipeName;
    }

    public static bool TrySend(string message, string pipeName = DefaultPipeName)
    {
        try
        {
            // Allows the running instance to take focus from the browser.
            AllowSetForegroundWindow(AnyProcess);

            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(2000);

            using var writer = new StreamWriter(pipe);
            writer.WriteLine(message);
            return true;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Listen(Action<string> onMessage) => _ = Task.Run(() => ListenAsync(_pipeName, onMessage, _cancellation.Token));

    public void Dispose()
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    private static async Task ListenAsync(string pipeName, Action<string> onMessage, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.WaitForConnectionAsync(cancellation);

                using var reader = new StreamReader(pipe);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));

                var message = await reader.ReadLineAsync(timeout.Token);
                if (message is { Length: > 0 and <= MaxMessageLength })
                {
                    onMessage(message);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or UnauthorizedAccessException)
            {
                Log.Warn("A message from a second launch was lost", ("reason", exception.Message));
                await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
            }
        }
    }
}

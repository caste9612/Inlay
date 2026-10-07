using System.IO.Pipes;

namespace Quiver.App.Services;

/// <summary>
/// Keeps Quiver to one instance per user. A second Quiver.exe hands its command (toggle, show, …) to the
/// running one through a named pipe and exits: this is how a GlazeWM keybinding can drive Quiver.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private static readonly string Name = $"Quiver.{Environment.UserName}";
    private readonly Mutex _mutex;

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: false, $@"Local\{Name}", out var createdNew);
        IsFirstInstance = createdNew;
    }

    public bool IsFirstInstance { get; }

    /// <summary>Raised on a background thread.</summary>
    public event Action<string>? CommandReceived;

    public void StartListening(CancellationToken cancellationToken) =>
        _ = Task.Run(() => ListenAsync(cancellationToken), cancellationToken);

    public static bool Send(string command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(command);
            return true;
        }
        catch (Exception e) when (e is TimeoutException or IOException)
        {
            Log.Write($"Could not reach the running instance: {e.Message}");
            return false;
        }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    Name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server);
                var command = await reader.ReadLineAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(command))
                    CommandReceived?.Invoke(command.Trim().ToLowerInvariant());
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException e)
            {
                Log.Write($"Command pipe error: {e.Message}");
                await Task.Delay(500, CancellationToken.None);
            }
        }
    }

    public void Dispose() => _mutex.Dispose();
}

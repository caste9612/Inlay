using System.IO.Pipes;

namespace Inlay.App.Services;

/// <summary>
/// Keeps Inlay to one instance per user. A second Inlay.exe hands its command (toggle, show, sort…) to the
/// running one through a named pipe and exits: this is how a GlazeWM keybinding, or «Invia a Inlay» in Explorer,
/// can drive Inlay.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private static readonly string Name = $"Inlay.{Environment.UserName}";
    private readonly Mutex _mutex;

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: false, $@"Local\{Name}", out var createdNew);
        IsFirstInstance = createdNew;
    }

    public bool IsFirstInstance { get; }

    /// <summary>Raised on a background thread: the command, and what came with it, like the files to sort.</summary>
    public event Action<string, IReadOnlyList<string>>? CommandReceived;

    public void StartListening(CancellationToken cancellationToken) =>
        _ = Task.Run(() => ListenAsync(cancellationToken), cancellationToken);

    public static bool Send(string command, IReadOnlyList<string>? arguments = null)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            Protocol.Write(writer, command, arguments ?? []);
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
                var (command, arguments) = await Protocol.ReadAsync(reader, cancellationToken);
                if (command.Length > 0)
                    CommandReceived?.Invoke(command, arguments);
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

    /// <summary>
    /// What goes through the pipe: a line with the command, then a line for each argument. A path can hold spaces,
    /// never a line break.
    /// </summary>
    internal static class Protocol
    {
        public static void Write(TextWriter writer, string command, IReadOnlyList<string> arguments)
        {
            writer.WriteLine(command);
            foreach (var argument in arguments)
                writer.WriteLine(argument);
        }

        /// <returns>The command in lower case, and the arguments as they came; an empty command when nothing was sent.</returns>
        public static async Task<(string Command, IReadOnlyList<string> Arguments)> ReadAsync(TextReader reader, CancellationToken cancellationToken)
        {
            var command = (await reader.ReadLineAsync(cancellationToken))?.Trim().ToLowerInvariant() ?? "";
            var arguments = new List<string>();
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (line.Length > 0)
                    arguments.Add(line);
            }
            return (command, arguments);
        }
    }
}

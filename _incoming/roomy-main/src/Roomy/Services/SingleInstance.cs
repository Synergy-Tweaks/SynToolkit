using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace Roomy.Services;

/// <summary>One interactive instance per user and privilege level.</summary>
public sealed class SingleInstance : IDisposable
{
    readonly Mutex _mutex;
    readonly string _pipeName;
    readonly CancellationTokenSource _stop = new();
    public bool IsPrimary { get; }

    public SingleInstance(bool elevated, bool demo) : this(InstanceName(elevated, demo)) { }

    static string InstanceName(bool elevated, bool demo)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User?.Value ?? Environment.UserName;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return $"Roomy.{user}.{process.SessionId}.{(elevated ? "admin" : "user")}{(demo ? ".demo" : "")}";
    }

    internal SingleInstance(string name)
    {
        _pipeName = name;
        _mutex = new Mutex(true, @"Local\" + _pipeName, out var created);
        IsPrimary = created;
    }

    public void Listen(Action<string> received) => _ = ListenAsync(received);

    async Task ListenAsync(Action<string> received)
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var reader = new StreamReader(pipe, Encoding.UTF8);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                // All commands are short, fixed strings. Do not accept arbitrary paths or arguments.
                var command = new StringBuilder();
                var buffer = new char[1];
                while (command.Length < 64 && await reader.ReadAsync(buffer.AsMemory(), timeout.Token) > 0)
                {
                    if (buffer[0] == '\n') { received(command.ToString().Trim()); break; }
                    command.Append(buffer[0]);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException)
            {
                try { await Task.Delay(200, _stop.Token); } catch (OperationCanceledException) { }
            }
        }
    }

    public async Task<bool> SendAsync(string command)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(3000);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            await writer.WriteLineAsync(command);
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException) { return false; }
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
        // The asynchronous listener still observes this source while shutting down.
    }
}

using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace EmilsClaudeLaunchpad.Startup;

// Background named-pipe listener used by the running instance to accept "wake" requests
// from a second launch. Owned by TrayAppContext for the life of the app.
public sealed class IpcServer : IDisposable
{
    public const string PipeName = "EmilsClaudeLaunchpad-ipc";
    public const string ShowCommand = "show";

    private readonly CancellationTokenSource _cts = new();
    private readonly Action _onShow;
    private readonly Task _loop;
    private bool _disposed;

    public IpcServer(Action onShow)
    {
        _onShow = onShow;
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);

                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (string.Equals(line, ShowCommand, StringComparison.Ordinal))
                    _onShow();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Swallow and re-loop. A broken pipe / IO error from a misbehaving client
                // shouldn't kill the listener — next iteration spins up a fresh server.
            }
        }
    }

    // Best-effort ping from a second launch. Returns true if the running instance accepted
    // the connection. Also grants foreground rights to the running instance so its launcher
    // form can actually steal focus when shown (otherwise Windows blocks SetForegroundWindow
    // and the taskbar just flashes).
    public static bool TryWakeRunningInstance(int timeoutMs = 800)
    {
        try
        {
            // Find the already-running instance's PID so we can hand it foreground rights
            // BEFORE we exit. SingleInstance guarantees at most one peer process.
            var ourPid = Environment.ProcessId;
            var ourName = Process.GetCurrentProcess().ProcessName;
            var peer = Process.GetProcessesByName(ourName)
                .FirstOrDefault(p => p.Id != ourPid);
            if (peer is not null)
                AllowSetForegroundWindow(peer.Id);

            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(ShowCommand);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _cts.Cancel();
            // Brief wait so the loop unwinds before we dispose the CTS — avoids a race where
            // the in-flight ReadLineAsync(ct) hits a disposed token.
            _loop.Wait(500);
        }
        catch { /* best-effort */ }
        _cts.Dispose();
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Timers;
using MeetingNotes.Models;

namespace MeetingNotes.Services;

/// <summary>
/// Polls the Windows foreground window every 2 seconds. When the active process
/// matches one of the user-configured "watched apps" a notification event fires.
/// </summary>
public class ActiveWindowMonitor : IDisposable
{
    private readonly AppSettings _settings;
    private System.Timers.Timer? _timer;
    private string? _lastNotifiedProcess;
    private DateTime _lastNotifyTime = DateTime.MinValue;

    public event EventHandler<string>? WatchedAppActivated;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public ActiveWindowMonitor(AppSettings settings) => _settings = settings;

    /// <summary>Start the background polling loop.</summary>
    public void Start()
    {
        _timer = new System.Timers.Timer(2000) { AutoReset = true };
        _timer.Elapsed += OnTick;
        _timer.Start();
    }

    public void Stop() => _timer?.Stop();

    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        if (!_settings.AppWatcherEnabled) return;
        if (string.IsNullOrWhiteSpace(_settings.WatchedApps)) return;

        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return;

            var process = Process.GetProcessById((int)pid);
            var procName = process.ProcessName.ToLowerInvariant();

            var watchList = _settings.WatchedApps
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(a => a.ToLowerInvariant());

            foreach (var watched in watchList)
            {
                if (!procName.Contains(watched) && !watched.Contains(procName))
                    continue;

                // 5-minute cooldown per process to avoid re-triggering immediately
                if (_lastNotifiedProcess == procName &&
                    (DateTime.Now - _lastNotifyTime).TotalMinutes < 5)
                    break;

                _lastNotifiedProcess = procName;
                _lastNotifyTime = DateTime.Now;
                WatchedAppActivated?.Invoke(this, process.ProcessName);
                break;
            }
        }
        catch { /* process may have exited between GetForegroundWindow and GetProcessById */ }
    }

    public void Dispose() => _timer?.Dispose();
}

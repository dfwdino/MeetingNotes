using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace MeetingNotes.Views;

public partial class RecordingPromptToast : Window
{
    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);

    // SND_ALIAS | SND_ASYNC — play the named system sound without blocking
    private const uint SND_ALIAS = 0x00010000;
    private const uint SND_ASYNC = 0x00000001;

    public event EventHandler? StartRecordingRequested;

    private readonly DispatcherTimer _countdown = new();
    private double _elapsed;
    private const double TotalMs = 8_000;

    public RecordingPromptToast(string appName)
    {
        InitializeComponent();
        AppNameText.Text = appName;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Snap to bottom-right of the work area
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width  - 16;
        Top  = area.Bottom - Height - 16;

        // Play the Windows notification chime
        PlaySound("SystemNotification", IntPtr.Zero, SND_ALIAS | SND_ASYNC);

        // Start the countdown bar at full width
        CountdownBar.Width = ToastCard.ActualWidth;

        _countdown.Interval = TimeSpan.FromMilliseconds(80);
        _countdown.Tick += (_, _) =>
        {
            _elapsed += 80;
            var fraction = Math.Max(0.0, 1.0 - _elapsed / TotalMs);
            CountdownBar.Width = fraction * ToastCard.ActualWidth;
            if (_elapsed >= TotalMs) Dismiss();
        };
        _countdown.Start();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        _countdown.Stop();
        StartRecordingRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void DismissButton_Click(object sender, RoutedEventArgs e) => Dismiss();

    private void ToastCard_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Dismiss()
    {
        _countdown.Stop();
        Close();
    }
}

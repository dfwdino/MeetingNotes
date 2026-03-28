using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MeetingNotes.Data;
using MeetingNotes.Models;
using MeetingNotes.Services;
using MeetingNotes.Views;
using MeetingNotes.ViewModels;
using System.Windows;
using System.Windows.Forms;

namespace MeetingNotes;

public partial class App : System.Windows.Application
{
    private static IServiceProvider? _services;
    public static System.Windows.Forms.NotifyIcon? TrayIcon { get; private set; }

    public static T GetService<T>() where T : notnull =>
        (T)_services!.GetService(typeof(T))!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Load settings FIRST — DB path, audio folder, etc. all come from here
        var settings = SettingsService.Load();

        _services = BuildServices(settings);

        // Copy loaded values into the DI singleton so all services see them
        UpdateSingleton(settings);

        // Initialize database (creates tables / runs migrations)
        var db = GetService<DatabaseService>();
        await db.InitializeAsync();

        // Configure Ollama with saved settings
        var ollama = GetService<OllamaService>();
        ollama.Configure(settings.OllamaServerUrl, settings.OllamaModel);

        // Check if Whisper model exists — show setup if not
        var whisperModelPath = Path.Combine(settings.WhisperCacheFolder,
            $"ggml-{settings.WhisperModel.ToLower()}.bin");

        if (!File.Exists(whisperModelPath))
        {
            var setup = GetService<SetupView>();
            if (setup.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
        }

        // Show main window
        var mainWindow = GetService<MainWindow>();
        SetupTrayIcon(mainWindow, settings);
        mainWindow.Show();

        // Start the active-window monitor; it self-gates on AppWatcherEnabled each tick
        var monitor = GetService<ActiveWindowMonitor>();
        monitor.WatchedAppActivated += (_, appName) =>
        {
            mainWindow.Dispatcher.Invoke(() =>
            {
                var toast = new Views.RecordingPromptToast(appName);
                toast.StartRecordingRequested += async (_, _) =>
                    await mainWindow.StartRecordingForWatcher(appName);
                toast.Show();
            });
        };
        monitor.Start();
    }

    private static void EnsureCleanDatabaseRegistration(string newMdfPath)
    {
        try
        {
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(
                "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT physical_name FROM sys.master_files
                                 WHERE database_id = DB_ID('MeetingNotes') AND file_id = 1";
            var existingPath = cmd.ExecuteScalar() as string;

            if (existingPath == null) return; // never registered — nothing to do

            bool pathMismatch = !string.Equals(existingPath, newMdfPath,
                                    StringComparison.OrdinalIgnoreCase);
            bool fileMissing  = !File.Exists(existingPath); // clean-slate: file deleted but registration remains

            if (!pathMismatch && !fileMissing) return; // registration is clean — leave it alone

            // Registration is stale (wrong path or missing file) — drop it so we can attach fresh
            // ALTER first for a clean disconnect; if the DB is in a bad state it may fail — that's OK
            try
            {
                cmd.CommandText = "ALTER DATABASE [MeetingNotes] SET SINGLE_USER WITH ROLLBACK IMMEDIATE";
                cmd.ExecuteNonQuery();
            }
            catch { /* DB may be suspect/unavailable due to missing file — skip, DROP still works */ }

            try
            {
                cmd.CommandText = "DROP DATABASE [MeetingNotes]";
                cmd.ExecuteNonQuery();
            }
            catch { /* already gone */ }
        }
        catch { /* LocalDB not available or DB never existed — nothing to clean up */ }
    }

    private static IServiceProvider BuildServices(AppSettings settings)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        // DB folder comes from settings.json (already resolved to a full path by SettingsService)
        var dbFolder   = settings.DatabaseFolder;
        Directory.CreateDirectory(dbFolder);
        var newMdfPath = Path.Combine(dbFolder, "MeetingNotes.mdf");

        // Drop stale LocalDB registration when path changed OR file was deleted (clean slate)
        EnsureCleanDatabaseRegistration(newMdfPath);

        var connectionString = $"Server=(localdb)\\MSSQLLocalDB;Database=MeetingNotes;Trusted_Connection=True;AttachDbFilename={newMdfPath}";

        services.AddDbContext<AppDbContext>(opt =>
            opt.UseSqlServer(connectionString));

        // Services
        services.AddSingleton<DatabaseService>();
        services.AddSingleton<AudioCaptureService>();
        services.AddSingleton<TranscriptionService>();
        services.AddSingleton<OllamaService>();
        services.AddSingleton<AppSettings>();
        services.AddSingleton<ActiveWindowMonitor>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<RecordingViewModel>();
        services.AddTransient<ProcessingViewModel>();

        // Views
        services.AddSingleton<MainWindow>();
        services.AddTransient<SetupView>();
        services.AddTransient<MeetingDetailView>();
        services.AddTransient<RecordingView>();
        services.AddTransient<ProcessingView>();
        services.AddTransient<SettingsView>();

        return services.BuildServiceProvider();
    }

    private static void UpdateSingleton(AppSettings loaded)
    {
        // Copy loaded values into the DI singleton so all services see current settings
        var singleton = GetService<AppSettings>();
        foreach (var prop in typeof(AppSettings).GetProperties().Where(p => p.CanWrite))
            prop.SetValue(singleton, prop.GetValue(loaded));
    }

    private static void SetupTrayIcon(MainWindow mainWindow, AppSettings settings)
    {
        TrayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Meeting Notes",
            Visible = true
        };

        // Load the app icon from the embedded resource
        try
        {
            var sri = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/MeetingNotes.ico"));
            TrayIcon.Icon = new System.Drawing.Icon(sri!.Stream);
        }
        catch
        {
            TrayIcon.Icon = System.Drawing.SystemIcons.Application;
        }

        var menu = new System.Windows.Forms.ContextMenuStrip();
        var showItem   = menu.Items.Add("Show");
        var statusItem = menu.Items.Add("Not recording");
        menu.Items.Add("-");
        var exitItem   = menu.Items.Add("Exit");

        statusItem!.Enabled = false;

        showItem!.Click  += (_, _) => { mainWindow.Show(); mainWindow.Activate(); };
        exitItem!.Click  += (_, _) => { TrayIcon.Visible = false; Current.Shutdown(); };

        TrayIcon.ContextMenuStrip = menu;
        TrayIcon.DoubleClick += (_, _) => { mainWindow.Show(); mainWindow.Activate(); };
    }

    public static void UpdateTrayRecordingStatus(bool isRecording, string timerText = "")
    {
        if (TrayIcon is null) return;
        if (isRecording)
        {
            TrayIcon.Text = $"Meeting Notes — Recording {timerText}";
            if (TrayIcon.ContextMenuStrip?.Items[1] is System.Windows.Forms.ToolStripItem item)
                item.Text = $"● Recording  {timerText}";
        }
        else
        {
            TrayIcon.Text = "Meeting Notes";
            if (TrayIcon.ContextMenuStrip?.Items[1] is System.Windows.Forms.ToolStripItem item)
                item.Text = "Not recording";
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TrayIcon?.Dispose();
        base.OnExit(e);
    }
}

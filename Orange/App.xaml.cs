using System.Windows;
using Orange.Core;
using Orange.Core.Updates;
using Orange.Input;
using Orange.UI;
using Forms = System.Windows.Forms;

namespace Orange;

public partial class App : Application
{
    private Forms.NotifyIcon? _tray;
    private DragWatcher? _watcher;
    private WheelWindow? _wheel;
    private Mutex? _singleInstance;

    public static App Instance => (App)Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Diagnostics run and exit before the single-instance check, so they work while Orange is open.
        if (e.Args.Length >= 2 && e.Args[0] == "--desktop-dump")
        {
            var probe = e.Args.SkipWhile(a => a != "--probe").Skip(1).FirstOrDefault();
            Shutdown(Orange.Desktop.Shell.DesktopDump.Run(e.Args[1], probe));
            return;
        }

        // Windows blocks drag & drop from Explorer into elevated apps (UIPI),
        // so if we were started as admin, restart as a normal user via Explorer.
        if (IsElevated() && !e.Args.Contains("--allow-admin"))
        {
            System.Diagnostics.Process.Start("explorer.exe", $"\"{Environment.ProcessPath}\"");
            Shutdown();
            return;
        }

        _singleInstance = new Mutex(true, "Orange.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        Tools.Initialize();
        PdfSharp.Fonts.GlobalFontSettings.UseWindowsFontsUnderWindows = true; // Arial etc. for page numbers and watermarks

        _wheel = new WheelWindow();
        _watcher = new DragWatcher();
        _watcher.WheelRequested += (mode, x, y) => _wheel.ShowAt(mode, x, y);
        _watcher.ModeChanged += mode => { if (_wheel.IsVisible) _wheel.SetMode(mode); };
        _watcher.DragEnded += () => _wheel.HideIfIdle();
        _watcher.CancelRequested += () => _wheel.HideWheel();
        _watcher.MouseDown += (x, y) => _wheel.OnGlobalMouseDown(x, y);
        _watcher.Start();

        CreateTray();
        Settings.Changed += BuildTrayMenu; // new language → new menu texts
        _ = Orange.Core.Share.PhoneBridge.StartAsync();
        AppUpdater.AvailableChanged += () => Dispatcher.BeginInvoke(BuildTrayMenu);
        StartUpdateChecks();

        var s = Settings.Current;
        Notify(Loc.T("Orange is running"), Loc.F("Drag a file and press {0} (convert), {1} (tools) or {2} (actions).",
            Settings.KeyName(s.ConvertKey), Settings.KeyName(s.ToolsKey), Settings.KeyName(s.ActionsKey)), force: true);
    }

    // ── Updates ───────────────────────────────────────────────────────────

    private System.Windows.Threading.DispatcherTimer? _updateTimer;
    private Version? _announced;

    /// <summary>First check a minute after start (don't slow down login), then every 6 hours.</summary>
    private void StartUpdateChecks()
    {
        _updateTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(6);
            if (Settings.Current.AutoCheckUpdates) await CheckForUpdateAsync(quiet: true);
        };
        _updateTimer.Start();
    }

    /// <summary>Checks GitHub. Quiet checks only speak up once per new version (and stay silent when offline).</summary>
    public async Task CheckForUpdateAsync(bool quiet)
    {
        try
        {
            var release = await AppUpdater.CheckAsync();
            if (release == null)
            {
                if (!quiet) Notify("Orange", Loc.F("You have the latest version ({0}).", AppUpdater.Current), force: true);
            }
            else if (!quiet || release.Version != _announced)
            {
                _announced = release.Version;
                Notify(Loc.F("Orange {0} is available", release.Version), Loc.T("Click here to update."), force: true,
                    onClick: () => _ = InstallUpdateAsync());
            }
        }
        catch (Exception ex)
        {
            JobRunner.Log($"Update check failed: {ex.Message}");
            if (!quiet) Notify("Orange", Loc.T("Could not check for updates. Are you online?"), error: true);
        }
    }

    public async Task InstallUpdateAsync()
    {
        if (AppUpdater.Available is not { } release) return;
        try
        {
            Notify(Loc.F("Updating to Orange {0}…", release.Version), Loc.T("Orange will restart by itself in a moment."), force: true);
            await AppUpdater.InstallAsync(release, null);
        }
        catch (Exception ex)
        {
            JobRunner.Log($"FAIL update: {ex}");
            Notify(Loc.T("Update failed"), ex.Message, error: true);
        }
    }

    private void CreateTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Icon = IconFactory.CreateTrayIcon(),
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => PickAndShowMenu();
        // What a click on the notification does depends on which notification it was.
        _tray.BalloonTipClicked += (_, _) => (_balloonClick ?? JobRunner.RevealLastOutput)();
        BuildTrayMenu();
    }

    private void BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        if (AppUpdater.Available is { } update)
        {
            var item = menu.Items.Add(Loc.F("Update to Orange {0}", update.Version), null, (_, _) => _ = InstallUpdateAsync());
            item.Font = new System.Drawing.Font(item.Font, System.Drawing.FontStyle.Bold);
            menu.Items.Add(new Forms.ToolStripSeparator());
        }
        menu.Items.Add(Loc.T("Convert files…"), null, (_, _) => PickAndShowMenu());
        menu.Items.Add(Loc.T("Connect phone…"), null, (_, _) => PairWindow.ShowSingle());
        menu.Items.Add(Loc.T("Settings…"), null, (_, _) => SettingsWindow.ShowSingle());
        menu.Items.Add(Loc.T("Open output log"), null, (_, _) => JobRunner.OpenLog());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Quit Orange"), null, (_, _) => Shutdown());

        var old = _tray!.ContextMenuStrip;
        _tray.ContextMenuStrip = menu;
        _tray.Text = Loc.T("Orange — file converter");
        old?.Dispose();
    }

    // Fallback flow without dragging: pick files, then open the wheel in the middle of the screen.
    private void PickAndShowMenu()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = Loc.T("Choose files for Orange") };
        if (dlg.ShowDialog() != true) return;
        var pos = Forms.Cursor.Position;
        _wheel!.ShowForFiles(dlg.FileNames, WheelMode.Convert, pos.X, pos.Y);
    }

    private static bool IsElevated()
    {
        using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private Action? _balloonClick;

    /// <summary>Tray notification. Errors always show; other messages respect the Notifications setting.
    /// <paramref name="onClick"/> runs when the notification is clicked (default: open the last output's folder).</summary>
    public void Notify(string title, string text, bool error = false, bool force = false, Action? onClick = null)
    {
        if (!error && !force && !Settings.Current.Notifications) return;
        _balloonClick = onClick;
        _tray?.ShowBalloonTip(3000, title, text, error ? Forms.ToolTipIcon.Error : Forms.ToolTipIcon.Info);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _watcher?.Dispose();
        // Off the UI thread: waiting here for a task that needs the UI thread would deadlock.
        Task.Run(Orange.Core.Share.PhoneBridge.StopAsync).Wait(2000);
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}

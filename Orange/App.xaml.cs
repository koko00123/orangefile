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
    private Orange.Desktop.Engine.DesktopEngine? _desktop;
    private Orange.UI.Desktop.EditSession? _desktopEdit;
    private Orange.UI.Desktop.HotkeyWindow? _desktopHotkey;
    private Orange.UI.Desktop.DesktopEvents? _desktopEvents;

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
        if (e.Args.Length >= 2 && e.Args[0] == "--desktop-backup")
        {
            Shutdown(Orange.Desktop.Engine.DesktopDiagnostics.Backup(e.Args[1]));
            return;
        }
        if (e.Args.Length >= 2 && e.Args[0] == "--desktop-restore")
        {
            Shutdown(Orange.Desktop.Engine.DesktopDiagnostics.Restore(e.Args[1]));
            return;
        }
        if (e.Args.Length >= 4 && e.Args[0] == "--desktop-preview" && int.TryParse(e.Args[2], out var seconds))
        {
            var mode = e.Args.SkipWhile(a => a != "--mode").Skip(1).FirstOrDefault();
            Orange.Desktop.Engine.DesktopDiagnostics.Preview(e.Args[1], seconds, e.Args[3], mode, Shutdown);
            return;   // keeps running until the preview timer calls Shutdown
        }

        // Windows blocks drag & drop from Explorer into elevated apps (UIPI),
        // so if we were started as admin, restart as a normal user via Explorer.
        if (IsElevated() && !e.Args.Contains("--allow-admin"))
        {
            System.Diagnostics.Process.Start("explorer.exe", $"\"{Environment.ProcessPath}\"");
            Shutdown();
            return;
        }

        // Try the Desktop module for one run without saving the setting.
        if (e.Args.Contains("--desktop")) Settings.Current.DesktopModuleEnabled = true;

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
        _watcher.MiddleClickRequested += (mode, x, y) =>
        {
            var files = ExplorerSelection.At(x, y);
            if (files.Count > 0) _wheel.ShowForFiles(files, mode, x, y);
        };
        _watcher.Start();

        CreateTray();
        StartDesktopModule();
        Settings.Changed += BuildTrayMenu; // new language → new menu texts
        _ = Orange.Core.Share.PhoneBridge.StartAsync();
        AppUpdater.AvailableChanged += () => Dispatcher.BeginInvoke(BuildTrayMenu);
        StartUpdateChecks();

        var s = Settings.Current;
        Notify(Loc.T("Orange is running"), Loc.F("Drag a file and press {0} (convert), {1} (tools) or {2} (actions).",
            Settings.KeyName(s.ConvertKey), Settings.KeyName(s.ToolsKey), Settings.KeyName(s.ActionsKey)), force: true);
    }

    /// <summary>Desktop boxes (off by default). Failures are logged and leave the desktop as it was.</summary>
    private void StartDesktopModule()
    {
        if (!Settings.Current.DesktopModuleEnabled) return;
        _desktop = new Orange.Desktop.Engine.DesktopEngine(new Orange.Desktop.Shell.ShellDesktopIcons(),
            new Orange.Desktop.Shell.Win32Monitors(), Orange.Desktop.Store.DesktopStore.Default());
        _desktop.Start();

        _desktopEvents = new Orange.UI.Desktop.DesktopEvents(_desktop, () => _desktopEdit?.IsOpen == true);
        _desktopEvents.Start();
        if (_watcher is not null) _watcher.LeftButtonUp += _desktopEvents.OnLeftButtonUp;

        _desktopHotkey = new Orange.UI.Desktop.HotkeyWindow();
        _desktopHotkey.Pressed += OnDesktopHotkey;
        RegisterDesktopHotkeys();
        BuildTrayMenu();
    }

    /// <summary>Stops the Desktop module. Boxes disappear; icons stay where they are and no file is touched.</summary>
    private void StopDesktopModule()
    {
        if (_desktop is null) return;
        try
        {
            if (_desktopEdit is { IsOpen: true }) _desktopEdit.Close();
            _desktopEdit = null;
            // Hidden icons would stay hidden with no menu to bring them back.
            if (_desktop.IconsHidden) _desktop.SetIconsHidden(false);
        }
        catch (Exception ex) { JobRunner.Log($"Desktop: could not close cleanly: {ex.Message}"); }
        if (_watcher is not null && _desktopEvents is not null) _watcher.LeftButtonUp -= _desktopEvents.OnLeftButtonUp;
        _desktopHotkey?.Dispose();
        _desktopEvents?.Dispose();
        _desktop.Dispose();
        (_desktopHotkey, _desktopEvents, _desktop) = (null, null, null);
    }

    /// <summary>Applies the Desktop settings (Settings window): starts, stops or restarts the module.</summary>
    public void RestartDesktopModule()
    {
        try
        {
            StopDesktopModule();
            StartDesktopModule();
        }
        catch (Exception ex) { JobRunner.Log($"Desktop: could not restart the module: {ex}"); }
        BuildTrayMenu();
    }

    // Hotkey ids: edit mode, hide icons, then one per layout (by position in the list).
    private const int HotkeyEdit = 1, HotkeyHide = 2, HotkeyLayoutBase = 100;

    private void RegisterDesktopHotkeys()
    {
        if (_desktopHotkey is null || _desktop is null) return;
        _desktopHotkey.Register(HotkeyEdit, Settings.Current.DesktopEditHotkey);
        _desktopHotkey.Register(HotkeyHide, Settings.Current.DesktopHideHotkey);
        _desktopHotkey.UnregisterWhere(id => id >= HotkeyLayoutBase);
        var layouts = _desktop.Layouts;
        for (int i = 0; i < layouts.Count; i++)
            if (!string.IsNullOrEmpty(layouts[i].Hotkey)) _desktopHotkey.Register(HotkeyLayoutBase + i, layouts[i].Hotkey);
    }

    private void OnDesktopHotkey(int id)
    {
        if (_desktop is null) return;
        if (id == HotkeyEdit) ToggleDesktopEdit();
        else if (id == HotkeyHide) ToggleDesktopIcons();
        else if (id >= HotkeyLayoutBase && id - HotkeyLayoutBase < _desktop.Layouts.Count)
            ChangeLayouts(c => Orange.Desktop.Layout.LayoutOps.Switch(c, _desktop.Layouts[id - HotkeyLayoutBase].Name));
    }

    private void ToggleDesktopIcons()
    {
        if (_desktop is null) return;
        if (_desktopEdit is { IsOpen: true }) _desktopEdit.Close();
        try { _desktop.SetIconsHidden(!_desktop.IconsHidden); }
        catch (Exception ex) { JobRunner.Log($"Desktop: could not hide/show icons: {ex.Message}"); }
        BuildTrayMenu();
    }

    /// <summary>Applies a layout operation; closes edit mode first so it never edits the wrong layout.</summary>
    private bool ChangeLayouts(Func<Orange.Desktop.Model.DesktopConfig, Orange.Desktop.Model.DesktopConfig?> op, bool redraw = true)
    {
        if (_desktop is null) return false;
        if (_desktopEdit is { IsOpen: true }) _desktopEdit.Close();
        bool ok = _desktop.Change(op(_desktop.Config), redraw);
        RegisterDesktopHotkeys();
        BuildTrayMenu();
        return ok;
    }

    private Forms.ToolStripMenuItem BuildLayoutMenu()
    {
        var desktop = _desktop!;
        var root = new Forms.ToolStripMenuItem(Loc.T("Layout"));
        var active = desktop.Layout.Name;
        foreach (var layout in desktop.Layouts)
        {
            var name = layout.Name;
            var item = new Forms.ToolStripMenuItem(name, null, (_, _) => ChangeLayouts(c => Orange.Desktop.Layout.LayoutOps.Switch(c, name)))
            {
                Checked = name == active,
                ShortcutKeyDisplayString = layout.Hotkey,
            };
            root.DropDownItems.Add(item);
        }
        if (desktop.Layouts.Count > 0) root.DropDownItems.Add(new Forms.ToolStripSeparator());

        root.DropDownItems.Add(Loc.T("Save as new layout…"), null, (_, _) =>
        {
            var name = PromptWindow.Ask("New layout", "Layout name:");
            if (name is not null && !ChangeLayouts(c => Orange.Desktop.Layout.LayoutOps.SaveAs(c, name), redraw: false))
                Notify("Orange", Loc.T("That name is already used."), error: true);
        });
        if (desktop.Layouts.Count > 0)
        {
            root.DropDownItems.Add(Loc.T("Rename layout…"), null, (_, _) =>
            {
                var name = PromptWindow.Ask("Rename layout", "Layout name:", active);
                if (name is not null && !ChangeLayouts(c => Orange.Desktop.Layout.LayoutOps.Rename(c, active, name), redraw: false))
                    Notify("Orange", Loc.T("That name is already used."), error: true);
            });
            root.DropDownItems.Add(Loc.T("Layout shortcut…"), null, (_, _) =>
            {
                var key = PromptWindow.Ask("Layout shortcut", Loc.F("Shortcut for \"{0}\" (e.g. Ctrl+Alt+1):", active), desktop.Layout.Hotkey);
                if (key is null) return;
                if (!ChangeLayouts(c => Orange.Desktop.Layout.LayoutOps.SetHotkey(c, active, key), redraw: false))
                    Notify("Orange", Loc.T("That shortcut is not valid. Use something like Ctrl+Alt+1."), error: true);
            });
            if (!string.IsNullOrEmpty(desktop.Layout.Hotkey))
                root.DropDownItems.Add(Loc.T("Remove shortcut"), null, (_, _) =>
                    ChangeLayouts(c => Orange.Desktop.Layout.LayoutOps.SetHotkey(c, active, ""), redraw: false));
        }
        if (desktop.Layouts.Count > 1)
        {
            root.DropDownItems.Add(Loc.T("Delete layout"), null, (_, _) =>
            {
                var answer = MessageBox.Show(Loc.F("Delete the layout \"{0}\"? Its boxes are removed; icons stay on the desktop.", active),
                    "Orange", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                if (answer == MessageBoxResult.OK) ChangeLayouts(c => Orange.Desktop.Layout.LayoutOps.Delete(c, active));
            });
        }
        return root;
    }

    /// <summary>Enters or leaves edit mode (tray menu and hotkey).</summary>
    public void ToggleDesktopEdit()
    {
        if (_desktop is null) return;
        try
        {
            if (_desktopEdit is { IsOpen: true }) { _desktopEdit.Close(); return; }
            _desktopEdit = new Orange.UI.Desktop.EditSession(_desktop);
            _desktopEdit.Open();
        }
        catch (Exception ex) { JobRunner.Log($"Desktop: edit mode failed: {ex}"); }
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
        if (_desktop is not null)
        {
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(new Forms.ToolStripMenuItem(Loc.T("Edit desktop"), null, (_, _) => ToggleDesktopEdit())
            {
                ShortcutKeyDisplayString = Settings.Current.DesktopEditHotkey,
            });
            menu.Items.Add(BuildLayoutMenu());
            bool hidden = false;
            try { hidden = _desktop.IconsHidden; } catch { }
            menu.Items.Add(new Forms.ToolStripMenuItem(Loc.T("Hide desktop icons"), null, (_, _) => ToggleDesktopIcons())
            {
                Checked = hidden,
                ShortcutKeyDisplayString = Settings.Current.DesktopHideHotkey,
            });
            menu.Items.Add(new Forms.ToolStripSeparator());
        }
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
        _desktopHotkey?.Dispose();
        _desktopEvents?.Dispose();
        _desktop?.Dispose();
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

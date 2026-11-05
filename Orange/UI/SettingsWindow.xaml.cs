using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Orange.Core;
using Orange.Core.Updates;

namespace Orange.UI;

public partial class SettingsWindow : Window
{
    private static SettingsWindow? _open;

    private static readonly ModKey[] Keys = [ModKey.Shift, ModKey.Ctrl, ModKey.Alt];

    /// <summary>Shows the window, or brings the already-open one to the front.</summary>
    public static void ShowSingle()
    {
        if (_open != null)
        {
            _open.Activate();
            return;
        }
        _open = new SettingsWindow();
        _open.Closed += (_, _) => _open = null;
        _open.Show();
        _open.Activate();
    }

    private SettingsWindow()
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 40; // scroll instead of running off small screens
        var s = Settings.Current;

        LanguageBox.ItemsSource = Loc.Languages;
        LanguageBox.SelectedValue = s.Language;
        LanguageBox.SelectionChanged += (_, _) => ApplyTexts();

        AutostartBox.IsChecked = Autostart.IsEnabled;
        NotifyBox.IsChecked = s.Notifications;

        SizeSlider.ValueChanged += (_, e) => SizeValue.Text = $"{e.NewValue:0} %";
        SizeSlider.Value = Math.Round(s.WheelScale * 100);
        QualitySlider.ValueChanged += (_, e) => QualityValue.Text = $"{e.NewValue:0}";
        QualitySlider.Value = s.ImageQuality;

        foreach (var (box, key) in new[] { (ConvertKeyBox, s.ConvertKey), (ToolsKeyBox, s.ToolsKey), (ActionsKeyBox, s.ActionsKey) })
        {
            box.ItemsSource = Keys.Select(Settings.KeyName).ToList();
            box.SelectedIndex = Array.IndexOf(Keys, key);
            box.SelectionChanged += (_, _) => CheckKeys();
        }

        NextToOriginal.IsChecked = s.Output == OutputMode.NextToOriginal;
        ToFolder.IsChecked = s.Output == OutputMode.Folder;
        FolderBox.Text = s.OutputFolder;
        BrowseButton.Click += (_, _) => Browse();

        PhoneBox.IsChecked = s.PhoneSharing;
        InboxBox.Text = s.PhoneInboxOrDefault;
        InboxBrowse.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.In(Lang, "Files from the phone go to:") };
            if (dlg.ShowDialog(this) == true) InboxBox.Text = dlg.FolderName;
        };
        PairButton.Click += (_, _) => PairWindow.ShowSingle();

        DesktopBox.IsChecked = s.DesktopModuleEnabled;
        DesktopBox.Checked += (_, _) => DesktopOptions.IsEnabled = true;
        DesktopBox.Unchecked += (_, _) => DesktopOptions.IsEnabled = false;
        DesktopOptions.IsEnabled = s.DesktopModuleEnabled;
        EditHotkeyBox.Text = s.DesktopEditHotkey;
        HideHotkeyBox.Text = s.DesktopHideHotkey;
        EditHotkeyBox.TextChanged += (_, _) => CheckKeys();
        HideHotkeyBox.TextChanged += (_, _) => CheckKeys();

        VersionText.Text = $"Orange {AppUpdater.Current}";
        AutoUpdateBox.IsChecked = s.AutoCheckUpdates;
        CheckNowButton.Click += CheckNowClick;
        ToolsButton.Click += ToolsClick;
        if (AppUpdater.Available is { } pending)
            UpdateStatus.Text = Loc.F("Orange {0} is available", pending.Version);

        SaveButton.Click += (_, _) => Save();
        ApplyTexts();
        ShowEngines();
    }

    private string Lang => LanguageBox.SelectedValue as string ?? "en";

    private void ApplyTexts()
    {
        string T(string s) => Loc.In(Lang, s);
        Title = TitleText.Text = T("Orange Settings");
        GeneralHeader.Text = T("General");
        LanguageLabel.Text = T("Language");
        AutostartBox.Content = T("Start with Windows");
        NotifyBox.Content = T("Show notifications when a job finishes");
        WheelHeader.Text = T("Wheel");
        SizeLabel.Text = T("Size");
        ConvertKeyLabel.Text = T("Key for Convert");
        ToolsKeyLabel.Text = T("Key for Tools");
        ActionsKeyLabel.Text = T("Key for Actions");
        KeyWarning.Text = T("Each mode needs a different key.");
        OutputHeader.Text = T("Output");
        NextToOriginal.Content = T("Save next to the original file");
        ToFolder.Content = T("Save everything in this folder:");
        BrowseButton.Content = T("Browse…");
        QualityLabel.Text = T("Image quality (JPG, WebP, AVIF)");
        PhoneHeader.Text = T("Phone (Wi-Fi sharing)");
        PhoneBox.Content = T("Share files with my phone over Wi-Fi");
        InboxLabel.Text = T("Files from the phone go to:");
        InboxBrowse.Content = T("Browse…");
        PairButton.Content = T("Connect phone…");
        DesktopHeader.Text = T("Desktop (icon boxes)");
        DesktopBox.Content = T("Organize desktop icons into boxes");
        DesktopNote.Text = T("Only moves icons. Files are never moved, renamed or deleted.");
        RenderModeLabel.Text = T("Draw boxes");
        OrphanLabel.Text = T("Boxes of a disconnected monitor");
        EditHotkeyLabel.Text = T("Shortcut to edit the desktop");
        HideHotkeyLabel.Text = T("Shortcut to hide icons (optional)");
        HotkeyWarning.Text = T("Use a shortcut like Ctrl+Alt+D.");
        // Rebuilt so the items follow the language; the index maps to the enum value.
        int render = RenderModeBox.SelectedIndex >= 0 ? RenderModeBox.SelectedIndex : (int)Settings.Current.DesktopRenderMode;
        RenderModeBox.ItemsSource = new[] { T("Behind the icons"), T("As a frame") };
        RenderModeBox.SelectedIndex = render;
        int orphan = OrphanBox.SelectedIndex >= 0 ? OrphanBox.SelectedIndex : (int)Settings.Current.DesktopOrphanBoxes;
        OrphanBox.ItemsSource = new[] { T("Hide them"), T("Show on the main monitor") };
        OrphanBox.SelectedIndex = orphan;
        EnginesHeader.Text = T("Engines");
        UpdatesHeader.Text = T("Updates");
        AutoUpdateBox.Content = T("Check for new versions automatically");
        if (CheckNowButton.IsEnabled && AppUpdater.Available == null) CheckNowButton.Content = T("Check now");
        if (_toolUpdates.Count == 0) ToolsButton.Content = T("Check for tool updates");
        SaveButton.Content = T("Save");
        CancelButton.Content = T("Cancel");
        ShowEngines();
    }

    // Versions are read by running the tools, so they are cached for the life of the window.
    private readonly Dictionary<ToolUpdater.Tool, string?> _toolVersions = [];
    private List<(ToolUpdater.Tool Tool, string Latest)> _toolUpdates = [];

    private async void ShowEngines()
    {
        string T(string s) => Loc.In(Lang, s);
        foreach (var tool in ToolUpdater.All.Where(t => !_toolVersions.ContainsKey(t)))
            _toolVersions[tool] = await Task.Run(() => ToolUpdater.InstalledVersion(tool));

        string ToolDetail(ToolUpdater.Tool tool, string? path) =>
            path == null ? "" : $"{_toolVersions.GetValueOrDefault(tool) ?? "?"} · " +
                (path.StartsWith(ToolUpdater.Folder, StringComparison.OrdinalIgnoreCase) ? T("updated")
                 : path.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase) ? T("included")
                 : T("installed on this PC")); // the full path is in the tooltip

        var engines = new (string Name, bool Ok, string Detail, string? Path)[]
        {
            ("ffmpeg", Tools.Ffmpeg != null, Tools.Ffmpeg != null ? ToolDetail(ToolUpdater.Ffmpeg, Tools.Ffmpeg) : "", Tools.Ffmpeg),
            ("Pandoc", Tools.Pandoc != null, Tools.Pandoc != null ? ToolDetail(ToolUpdater.Pandoc, Tools.Pandoc) : "", Tools.Pandoc),
            ("Microsoft Word", Office.HasWord, "", null),
            ("Microsoft Excel", Office.HasExcel, "", null),
            ("Microsoft PowerPoint", Office.HasPowerPoint, "", null),
        };

        EngineList.Items.Clear();
        foreach (var (name, ok, detail, path) in engines)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock
            {
                Text = ok ? "✓" : "✗", Width = 20, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(ok ? Color.FromRgb(0x2E, 0x7D, 0x32) : Color.FromRgb(0xC6, 0x28, 0x28)),
            });
            row.Children.Add(new TextBlock { Text = name, Width = 160 });
            row.Children.Add(new TextBlock
            {
                Text = !ok ? T("not found") : detail.Length > 0 ? detail : T("found"),
                Foreground = Brushes.Gray, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 290, ToolTip = path,
            });
            EngineList.Items.Add(row);
        }
    }

    // ── Updates ───────────────────────────────────────────────────────────

    private async Task CheckAppAsync()
    {
        string T(string s) => Loc.In(Lang, s);
        CheckNowButton.IsEnabled = false;
        UpdateStatus.Text = T("Checking…");
        try
        {
            var release = await AppUpdater.CheckAsync();
            if (release == null)
            {
                UpdateStatus.Text = string.Format(T("You have the latest version ({0})."), AppUpdater.Current);
            }
            else
            {
                UpdateStatus.Text = string.Format(T("Orange {0} is available"), release.Version);
                CheckNowButton.Content = T("Update now");
                CheckNowButton.Click -= CheckNowClick;
                CheckNowButton.Click += (_, _) =>
                {
                    CheckNowButton.IsEnabled = false;
                    UpdateStatus.Text = string.Format(T("Updating to Orange {0}…"), release.Version);
                    _ = App.Instance.InstallUpdateAsync();
                };
            }
        }
        catch (Exception ex)
        {
            JobRunner.Log($"Update check failed: {ex.Message}");
            UpdateStatus.Text = T("Could not check for updates. Are you online?");
        }
        finally
        {
            CheckNowButton.IsEnabled = true;
        }
    }

    private async void CheckNowClick(object sender, RoutedEventArgs e) => await CheckAppAsync();

    private async void ToolsClick(object sender, RoutedEventArgs e)
    {
        string T(string s) => Loc.In(Lang, s);
        ToolsButton.IsEnabled = false;
        try
        {
            if (_toolUpdates.Count == 0)
            {
                // Step 1: compare what we have with what's published.
                ToolsStatus.Text = T("Checking…");
                var found = new List<(ToolUpdater.Tool, string)>();
                foreach (var tool in ToolUpdater.All)
                {
                    var latest = await tool.LatestAsync();
                    if (ToolUpdater.IsNewer(latest, _toolVersions.GetValueOrDefault(tool))) found.Add((tool, latest));
                }
                _toolUpdates = found;
                ToolsStatus.Text = found.Count == 0 ? T("All tools are up to date.")
                    : string.Join(", ", found.Select(f => $"{f.Item1.Name} {f.Item2}"));
                ToolsButton.Content = found.Count == 0 ? T("Check for tool updates") : T("Update tools");
                return;
            }

            // Step 2: download and install them, one after another.
            ToolsProgress.Visibility = Visibility.Visible;
            foreach (var (tool, latest) in _toolUpdates)
            {
                ToolsStatus.Text = string.Format(T("Downloading {0} {1}…"), tool.Name, latest);
                ToolsProgress.Value = 0;
                await tool.UpdateAsync(new Progress<double>(p => ToolsProgress.Value = p));
                _toolVersions.Remove(tool);
            }
            _toolUpdates = [];
            ToolsStatus.Text = T("Tools updated ✓");
            ToolsButton.Content = T("Check for tool updates");
            ShowEngines();
        }
        catch (Exception ex)
        {
            JobRunner.Log($"FAIL tool update: {ex}");
            ToolsStatus.Text = T("Update failed") + ": " + ex.Message;
        }
        finally
        {
            ToolsButton.IsEnabled = true;
            ToolsProgress.Visibility = Visibility.Collapsed;
        }
    }

    private bool CheckKeys()
    {
        var picked = new[] { ConvertKeyBox.SelectedIndex, ToolsKeyBox.SelectedIndex, ActionsKeyBox.SelectedIndex };
        bool ok = picked.Distinct().Count() == 3;
        KeyWarning.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;

        // The edit shortcut is required; the hide shortcut may be empty.
        bool hotkeysOk = Orange.Desktop.Engine.Hotkey.TryParse(EditHotkeyBox.Text.Trim(), out _, out _)
            && (HideHotkeyBox.Text.Trim().Length == 0 || Orange.Desktop.Engine.Hotkey.TryParse(HideHotkeyBox.Text.Trim(), out _, out _));
        HotkeyWarning.Visibility = hotkeysOk ? Visibility.Collapsed : Visibility.Visible;

        SaveButton.IsEnabled = ok && hotkeysOk;
        return ok && hotkeysOk;
    }

    private void Browse()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.In(Lang, "Choose a folder for the output files.") };
        if (Directory.Exists(FolderBox.Text)) dlg.InitialDirectory = FolderBox.Text;
        if (dlg.ShowDialog(this) != true) return;
        FolderBox.Text = dlg.FolderName;
        ToFolder.IsChecked = true;
    }

    private void Save()
    {
        if (!CheckKeys()) return;
        // Choosing "this folder" without a folder makes no sense — ask for one.
        if (ToFolder.IsChecked == true && !Directory.Exists(FolderBox.Text))
        {
            Browse();
            if (!Directory.Exists(FolderBox.Text)) return;
        }

        var s = Settings.Current.Clone();
        s.Language = Lang;
        s.Notifications = NotifyBox.IsChecked == true;
        s.WheelScale = SizeSlider.Value / 100;
        s.ConvertKey = Keys[ConvertKeyBox.SelectedIndex];
        s.ToolsKey = Keys[ToolsKeyBox.SelectedIndex];
        s.ActionsKey = Keys[ActionsKeyBox.SelectedIndex];
        s.Output = ToFolder.IsChecked == true ? OutputMode.Folder : OutputMode.NextToOriginal;
        s.OutputFolder = FolderBox.Text;
        s.ImageQuality = (int)QualitySlider.Value;
        s.AutoCheckUpdates = AutoUpdateBox.IsChecked == true;
        s.PhoneSharing = PhoneBox.IsChecked == true;
        s.PhoneInbox = InboxBox.Text;
        bool phoneChanged = s.PhoneSharing != Settings.Current.PhoneSharing;
        s.DesktopModuleEnabled = DesktopBox.IsChecked == true;
        s.DesktopRenderMode = (DesktopRenderMode)Math.Max(0, RenderModeBox.SelectedIndex);
        s.DesktopOrphanBoxes = (DesktopOrphanBoxes)Math.Max(0, OrphanBox.SelectedIndex);
        s.DesktopEditHotkey = EditHotkeyBox.Text.Trim();
        s.DesktopHideHotkey = HideHotkeyBox.Text.Trim();
        var old = Settings.Current;
        bool desktopChanged = s.DesktopModuleEnabled != old.DesktopModuleEnabled || s.DesktopRenderMode != old.DesktopRenderMode
            || s.DesktopOrphanBoxes != old.DesktopOrphanBoxes || s.DesktopEditHotkey != old.DesktopEditHotkey
            || s.DesktopHideHotkey != old.DesktopHideHotkey;
        Settings.Save(s);
        if (phoneChanged) _ = Orange.Core.Share.PhoneBridge.RestartAsync(); // starts or stops the server
        if (desktopChanged) App.Instance.RestartDesktopModule();             // starts, stops or redraws the boxes

        Autostart.Set(AutostartBox.IsChecked == true);
        Close();
    }
}

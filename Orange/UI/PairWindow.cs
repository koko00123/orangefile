using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Orange.Core;
using Orange.Core.Share;
using QRCoder;

namespace Orange.UI;

/// <summary>"Connect phone": QR code to open Orange on the iPhone, plus the steps and troubleshooting.</summary>
public sealed class PairWindow : Window
{
    private static PairWindow? _open;
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x73));
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x1F));

    public static void ShowSingle()
    {
        if (_open != null) { _open.Activate(); return; }
        _open = new PairWindow();
        _open.Closed += (_, _) => _open = null;
        _open.Show();
        _open.Activate();
    }

    private PairWindow()
    {
        Title = Loc.T("Connect your phone");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF8));
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        Build();
    }

    private void Build()
    {
        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock { Text = Loc.T("Connect your phone"), FontSize = 20, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = Loc.T("Your phone must be on the same Wi-Fi as this PC."),
            Foreground = Muted, Margin = new Thickness(0, 2, 0, 14),
        });

        var url = PhoneBridge.Url;
        if (!PhoneBridge.IsRunning || url == null)
        {
            root.Children.Add(Card(new TextBlock
            {
                Text = !Settings.Current.PhoneSharing ? Loc.T("Phone sharing is turned off in Settings.")
                     : url == null ? Loc.T("This PC is not connected to a network.")
                     : PhoneBridge.LastError ?? Loc.T("Phone sharing could not start."),
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick,
            }));
            Content = root;
            return;
        }

        // QR code
        var png = new PngByteQRCode(new QRCodeGenerator().CreateQrCode(url, QRCodeGenerator.ECCLevel.M)).GetGraphic(8);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new MemoryStream(png);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        var qr = new Image { Source = bmp, Width = 220, Height = 220, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(qr, BitmapScalingMode.NearestNeighbor);

        var qrPanel = new StackPanel();
        qrPanel.Children.Add(qr);
        qrPanel.Children.Add(new TextBox
        {
            Text = url, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Foreground = Muted, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0),
        });
        root.Children.Add(Card(qrPanel));

        // Steps
        root.Children.Add(Steps(
            Loc.T("1. Open the iPhone Camera and point it at the code."),
            Loc.T("2. Tap the link that appears. Orange opens in Safari."),
            Loc.T("3. Tap Share (□↑), then “Add to Home Screen”. Now Orange is an icon on your phone."),
            Loc.F("Send from the PC: drag a file, press {0} and drop it on “iPhone”.", Settings.KeyName(Settings.Current.ActionsKey)),
            Loc.F("Files from the phone arrive in: {0}", Settings.Current.PhoneInboxOrDefault)));

        // Share-menu shortcut (optional)
        var shortcut = new Expander
        {
            Header = Loc.T("Optional: “Send to Orange” in the iPhone Share menu"),
            Margin = new Thickness(0, 4, 0, 10),
            Content = Steps(
                Loc.T("In the Shortcuts app tap +, name it “Send to Orange”."),
                Loc.T("Tap ⓘ and turn on “Show in Share Sheet”."),
                Loc.T("Add “Repeat with Each” (Shortcut Input). Inside it, add “Get Contents of URL” with the address from the Orange page on your phone (Copy button)."),
                $"http://{PhoneBridge.LanAddress()}:{Settings.Current.PhonePort}/api/upload?k={Settings.Current.PhoneToken}",
                Loc.T("Method: POST · Request Body: Form · add a File field named “file” = Repeat Item."),
                Loc.T("Done. In Photos or Files, tap Share → Send to Orange.")),
        };
        root.Children.Add(shortcut);

        // Copy-to-PC shortcut (optional)
        root.Children.Add(new Expander
        {
            Header = Loc.T("Optional: copy text from the iPhone to this PC"),
            Margin = new Thickness(0, 0, 0, 10),
            Content = Steps(
                Loc.T("In the Shortcuts app tap +, name it “Copy to PC”, and add “Get Clipboard”."),
                Loc.T("Add “Get Contents of URL” with this address:"),
                $"http://{PhoneBridge.LanAddress()}:{Settings.Current.PhonePort}/api/clipboard?k={Settings.Current.PhoneToken}",
                Loc.T("Method: POST · Request Body: Form · add a Text field named “text” = Clipboard."),
                Loc.T("For images too, add a File field named “file” = Clipboard."),
                Loc.T("In iPhone Settings → Accessibility → Touch → Back Tap, set Double Tap to “Copy to PC”."),
                Loc.T("Done. Copy text on the iPhone, double-tap the back of the phone, and press Ctrl+V on the PC.")),
        });

        // Copy-from-PC shortcut (optional)
        root.Children.Add(new Expander
        {
            Header = Loc.T("Optional: copy text from this PC to the iPhone"),
            Margin = new Thickness(0, 0, 0, 10),
            Content = Steps(
                Loc.T("In the Shortcuts app tap +, name it “Copy from PC”, and add “Get Contents of URL” with this address:"),
                $"http://{PhoneBridge.LanAddress()}:{Settings.Current.PhonePort}/api/clipboard?k={Settings.Current.PhoneToken}",
                Loc.T("Leave Method as GET, then add “Copy to Clipboard”."),
                Loc.T("In iPhone Settings → Accessibility → Touch → Back Tap, set Triple Tap to “Copy from PC”."),
                Loc.T("Done. Press Ctrl+C on the PC, triple-tap the back of the phone, and paste on the iPhone.")),
        });

        // Buttons
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Left };
        buttons.Children.Add(Button(Loc.T("Open received folder"), () =>
        {
            Directory.CreateDirectory(Settings.Current.PhoneInboxOrDefault);
            Process.Start("explorer.exe", $"\"{Settings.Current.PhoneInboxOrDefault}\"");
        }));
        buttons.Children.Add(Button(Loc.T("Phone can't connect? Allow in firewall"), () =>
        {
            bool ok = PhoneBridge.OpenFirewall();
            MessageBox.Show(this, ok ? Loc.T("Done. Try again on the phone.") : Loc.T("The firewall was not changed."), "Orange");
        }));
        buttons.Children.Add(Button(Loc.T("New key"), async () =>
        {
            if (MessageBox.Show(this, Loc.T("Phones that are already connected will need to scan the new code. Continue?"),
                    "Orange", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            var s = Settings.Current.Clone();
            s.PhoneToken = Settings.NewToken();
            Settings.Save(s);
            await PhoneBridge.RestartAsync();
            Build();
        }));
        root.Children.Add(buttons);

        Content = root;
    }

    private static Border Card(UIElement child) => new()
    {
        Background = Brushes.White, CornerRadius = new CornerRadius(10), BorderBrush = new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE7)),
        BorderThickness = new Thickness(1), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12), Child = child,
    };

    private static StackPanel Steps(params string[] lines)
    {
        var panel = new StackPanel { Margin = new Thickness(2, 0, 0, 10) };
        foreach (var line in lines)
        {
            bool isUrl = line.StartsWith("http");
            panel.Children.Add(isUrl
                ? new TextBox { Text = line, IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 11, Margin = new Thickness(0, 2, 0, 4), Padding = new Thickness(4) }
                : new TextBlock { Text = line, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) });
        }
        return panel;
    }

    private static Button Button(string text, Action onClick)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 8) };
        b.Click += (_, _) => onClick();
        return b;
    }
}

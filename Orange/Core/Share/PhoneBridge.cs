using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Orange.Core.Share;

/// <summary>
/// "AirDrop" over Wi-Fi: a small private web server on the local network.
/// The phone opens it in Safari (via the QR code) to send files to the PC and to pick up
/// files the PC queued for it. Every request needs the secret key from the QR code.
/// </summary>
public static class PhoneBridge
{
    public record OutboxItem(string Id, string Path, string Name, long Size, DateTime Added);

    private static WebApplication? _server;
    private static readonly object OutboxLock = new();
    private static readonly string OutboxFile = System.IO.Path.Combine(Paths.AppData, "phone-outbox.json");
    private static List<OutboxItem> _outbox = LoadOutbox();

    public static bool IsRunning => _server != null;
    public static string? LastError { get; private set; }

    /// <summary>The address to open on the phone, including the secret key.</summary>
    public static string? Url => LanAddress() is { } ip
        ? $"http://{ip}:{Settings.Current.PhonePort}/?k={Uri.EscapeDataString(Settings.Current.PhoneToken)}"
        : null;

    // ── Server lifecycle ──────────────────────────────────────────────────

    public static async Task StartAsync()
    {
        if (_server != null || !Settings.Current.PhoneSharing) return;
        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k =>
            {
                k.ListenAnyIP(Settings.Current.PhonePort);
                k.Limits.MaxRequestBodySize = null; // phone videos can be large
            });
            builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = long.MaxValue);

            var app = builder.Build();
            app.Use(async (ctx, next) =>
            {
                bool upload = ctx.Request.Path.StartsWithSegments("/api/upload");
                if (ctx.Request.Path.StartsWithSegments("/icon.png") || Authorized(ctx))
                {
                    try { await next(); }
                    catch (Exception ex)
                    {
                        JobRunner.Log($"FAIL phone request {ctx.Request.Method} {ctx.Request.Path}: {ex.Message}");
                        if (!ctx.Response.HasStarted) ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    }
                }
                else
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    JobRunner.Log($"FAIL phone request rejected (wrong or missing key) from {ctx.Connection.RemoteIpAddress}: {ctx.Request.Method} {ctx.Request.Path}");
                }

                // Every upload attempt is logged, so Shortcut problems can be diagnosed from the PC.
                if (upload)
                    JobRunner.Log($"     upload from {ctx.Connection.RemoteIpAddress}: {ctx.Request.Method} {ctx.Request.ContentType ?? "(no type)"}, " +
                                  $"{ctx.Request.ContentLength?.ToString() ?? "?"} bytes, agent \"{ctx.Request.Headers.UserAgent}\" → {ctx.Response.StatusCode}");
            });

            app.MapGet("/", ctx =>
            {
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.Headers.CacheControl = "no-store";
                return ctx.Response.WriteAsync(PhonePage.Render(Settings.Current.PhoneToken, ctx.Request.Host.Value ?? ""));
            });
            app.MapGet("/icon.png", ctx =>
            {
                ctx.Response.ContentType = "image/png";
                return ctx.Response.Body.WriteAsync(UI.IconFactory.CreatePng(180)).AsTask();
            });
            app.MapGet("/api/files", () => Results.Json(Outbox().Select(o => new { o.Id, o.Name, o.Size, added = o.Added })));
            app.MapGet("/api/files/{id}", (string id, bool? inline) =>
            {
                var item = Outbox().FirstOrDefault(o => o.Id == id);
                if (item == null) return Results.NotFound();
                // inline=true lets Safari show photos/videos so they can be long-pressed → Save to Photos.
                return inline == true
                    ? Results.File(item.Path, MimeOf(item.Name), enableRangeProcessing: true)
                    : Results.File(item.Path, MimeOf(item.Name), item.Name, enableRangeProcessing: true);
            });
            app.MapDelete("/api/files/{id}", (string id) =>
            {
                Remove(id);
                return Results.NoContent();
            });
            // Cast to Delegate so it's treated as a route handler and the IResult is written (not discarded).
            app.MapPost("/api/upload", (Delegate)ReceiveAsync);
            app.MapPost("/api/clipboard", (Delegate)ReceiveClipboardAsync);
            app.MapGet("/api/clipboard", (Delegate)SendClipboardAsync);

            await app.StartAsync();
            _server = app;
            LastError = null;
            JobRunner.Log($"Phone sharing on {Url}");
        }
        catch (Exception ex)
        {
            LastError = ex is IOException or SocketException
                ? $"Port {Settings.Current.PhonePort} is already in use. Choose another port in Settings."
                : ex.Message;
            JobRunner.Log($"FAIL phone sharing: {ex}");
        }
    }

    public static async Task StopAsync()
    {
        var server = _server;
        _server = null;
        if (server != null) await server.DisposeAsync();
    }

    public static async Task RestartAsync()
    {
        await StopAsync();
        await StartAsync();
    }

    private static bool Authorized(HttpContext ctx)
    {
        var given = ctx.Request.Query["k"].ToString();
        if (given.Length == 0) given = ctx.Request.Headers["X-Orange-Key"].ToString();
        var expected = Encoding.UTF8.GetBytes(Settings.Current.PhoneToken);
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), expected);
    }

    // ── Phone → PC ────────────────────────────────────────────────────────

    /// <summary>Accepts multipart uploads (from the web page or an iOS Shortcut "Form" body)
    /// and raw bodies with ?name=… (Shortcut "File" body).</summary>
    private static async Task<IResult> ReceiveAsync(HttpContext ctx)
    {
        var inbox = Settings.Current.PhoneInboxOrDefault;
        Directory.CreateDirectory(inbox);
        var saved = new List<string>();

        if (ctx.Request.HasFormContentType)
        {
            var form = await ctx.Request.ReadFormAsync();
            foreach (var file in form.Files)
            {
                var target = Paths.Unique(System.IO.Path.Combine(inbox, SafeName(file.FileName)));
                await using var output = File.Create(target);
                await file.CopyToAsync(output);
                saved.Add(target);
            }
        }
        else
        {
            var name = ctx.Request.Query["name"].ToString();
            if (name.Length == 0) name = $"From iPhone {DateTime.Now:yyyy-MM-dd HH.mm.ss}{ExtensionFor(ctx.Request.ContentType)}";
            var target = Paths.Unique(System.IO.Path.Combine(inbox, SafeName(name)));
            await using (var output = File.Create(target))
                await ctx.Request.Body.CopyToAsync(output);
            saved.Add(target);
        }

        if (saved.Count == 0) return Results.BadRequest("No file received.");
        foreach (var s in saved) JobRunner.Log($"OK   From phone: {s}");

        _ = App.Instance.Dispatcher.BeginInvoke(() =>
        {
            JobRunner.Remember(saved[^1]);
            App.Instance.Notify(Loc.T("Received from your phone"),
                saved.Count == 1 ? System.IO.Path.GetFileName(saved[0]) : Loc.F("{0} files", saved.Count), force: true);
        });
        return Results.Ok(new { saved = saved.Select(System.IO.Path.GetFileName) });
    }

    /// <summary>Puts text or an image from the phone on the PC clipboard. Accepts form fields "file" and "text"
    /// (iOS Shortcut "Form" body; the shortcut can't tell which one the clipboard holds, so it sends both and a
    /// real image wins) and raw text bodies (the web page). The text itself is never logged: it may be a password.</summary>
    private static async Task<IResult> ReceiveClipboardAsync(HttpContext ctx)
    {
        const int maxTextLength = 4 * 1024 * 1024;
        var limit = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (limit != null) limit.MaxRequestBodySize = 64 * 1024 * 1024; // room for a full-size photo

        string text;
        if (ctx.Request.HasFormContentType)
        {
            var form = await ctx.Request.ReadFormAsync();
            text = form["text"].ToString();
            if (form.Files["file"] is { } file)
            {
                using var data = new MemoryStream();
                await file.CopyToAsync(data);
                if (Images.ForClipboard(data.ToArray()) is { } image)
                {
                    await OnClipboardAsync(() =>
                    {
                        System.Windows.Clipboard.SetImage(image);
                        return true;
                    });
                    _ = App.Instance.Dispatcher.BeginInvoke(() =>
                        App.Instance.Notify(Loc.T("Image copied from your phone"), Loc.T("Paste with Ctrl+V"), force: true));
                    JobRunner.Log($"OK   Clipboard image from phone: {image.PixelWidth}x{image.PixelHeight}");
                    return Results.Ok(new { width = image.PixelWidth, height = image.PixelHeight });
                }
            }
        }
        else
        {
            using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
            text = await reader.ReadToEndAsync();
        }
        if (text.Length == 0) return Results.BadRequest("No text received.");
        if (text.Length > maxTextLength) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        text = text.ReplaceLineEndings("\r\n");
        // Awaited so the phone hears about a failure.
        await OnClipboardAsync(() =>
        {
            System.Windows.Clipboard.SetText(text);
            return true;
        });
        _ = App.Instance.Dispatcher.BeginInvoke(() =>
            App.Instance.Notify(Loc.T("Copied from your phone"), Loc.F("{0} characters — paste with Ctrl+V", text.Length), force: true));
        JobRunner.Log($"OK   Clipboard from phone: {text.Length} characters");
        return Results.Ok(new { length = text.Length });
    }

    /// <summary>Gives the phone the text or image on the PC clipboard, with a notice on the PC every time. Copies
    /// that password managers mark as private are skipped: this travels as plain http. The content is never logged.</summary>
    private static async Task<IResult> SendClipboardAsync(HttpContext ctx)
    {
        ctx.Response.Headers.CacheControl = "no-store";
        var (text, png) = await OnClipboardAsync<(string, byte[]?)>(() =>
        {
            if (System.Windows.Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing")) return ("", null);
            if (System.Windows.Clipboard.ContainsText()) return (System.Windows.Clipboard.GetText(), null);
            return System.Windows.Clipboard.ContainsImage() && System.Windows.Clipboard.GetImage() is { } image
                ? ("", Images.FromClipboard(image))
                : ("", null);
        });
        if (text.Length == 0 && png == null) return Results.NoContent();

        _ = App.Instance.Dispatcher.BeginInvoke(() =>
            App.Instance.Notify("Orange", Loc.T("Your phone read the PC clipboard"), force: true));
        JobRunner.Log(png != null ? $"OK   Clipboard image to phone: {png.Length} bytes" : $"OK   Clipboard to phone: {text.Length} characters");
        return png != null ? Results.Bytes(png, "image/png") : Results.Text(text, "text/plain", Encoding.UTF8);
    }

    /// <summary>Runs on the UI thread, the only one where the clipboard works. Other programs can hold the
    /// clipboard for a moment, so a busy clipboard is retried for about a second, without blocking the UI.</summary>
    private static async Task<T> OnClipboardAsync<T>(Func<T> action)
    {
        for (int retries = 10; ; retries--)
        {
            try { return await App.Instance.Dispatcher.InvokeAsync(action); }
            catch (System.Runtime.InteropServices.ExternalException) when (retries > 0) { await Task.Delay(100); }
        }
    }

    private static string SafeName(string name)
    {
        name = System.IO.Path.GetFileName(name.Replace('/', '\\')); // never allow folders in the name
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) || name is "." or ".." ? "file" : name;
    }

    private static string ExtensionFor(string? contentType) => contentType?.Split(';')[0].Trim() switch
    {
        "image/jpeg" => ".jpg", "image/png" => ".png", "image/heic" => ".heic", "image/gif" => ".gif",
        "video/quicktime" => ".mov", "video/mp4" => ".mp4", "application/pdf" => ".pdf", "text/plain" => ".txt",
        _ => "",
    };

    // ── PC → Phone ────────────────────────────────────────────────────────

    /// <summary>Queues files for the phone. They stay listed until removed on the phone.</summary>
    public static void Send(IEnumerable<string> paths)
    {
        int added = 0;
        lock (OutboxLock)
        {
            foreach (var p in paths)
            {
                if (Directory.Exists(p) || !File.Exists(p)) continue; // folders: zip them first (Tools → ZIP)
                _outbox.RemoveAll(o => string.Equals(o.Path, p, StringComparison.OrdinalIgnoreCase));
                _outbox.Insert(0, new OutboxItem(Guid.NewGuid().ToString("N")[..12], p, System.IO.Path.GetFileName(p),
                    new FileInfo(p).Length, DateTime.Now));
                added++;
            }
            if (_outbox.Count > 100) _outbox.RemoveRange(100, _outbox.Count - 100);
            SaveOutbox();
        }
        App.Instance.Notify(Loc.T("Ready on your phone"),
            added == 0 ? Loc.T("Folders can't be sent. Zip them first.") : Loc.F("{0} file(s) — open Orange on your phone", added),
            error: added == 0, force: true);
    }

    private static List<OutboxItem> Outbox()
    {
        lock (OutboxLock)
        {
            // Forget files that were moved or deleted on the PC.
            if (_outbox.RemoveAll(o => !File.Exists(o.Path)) > 0) SaveOutbox();
            return _outbox.ToList();
        }
    }

    private static void Remove(string id)
    {
        lock (OutboxLock)
        {
            if (_outbox.RemoveAll(o => o.Id == id) > 0) SaveOutbox();
        }
    }

    private static List<OutboxItem> LoadOutbox()
    {
        try
        {
            if (File.Exists(OutboxFile))
                return JsonSerializer.Deserialize<List<OutboxItem>>(File.ReadAllText(OutboxFile)) ?? [];
        }
        catch { }
        return [];
    }

    private static void SaveOutbox() => File.WriteAllText(OutboxFile, JsonSerializer.Serialize(_outbox));

    // ── Network ───────────────────────────────────────────────────────────

    /// <summary>The PC's address on the home network: an up, non-virtual adapter with a gateway, Wi-Fi first.</summary>
    public static IPAddress? LanAddress()
    {
        string[] virtualHints = ["virtual", "vethernet", "hyper-v", "vmware", "virtualbox", "loopback", "bluetooth", "tap", "wsl", "vpn"];
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
                        && !virtualHints.Any(h => (n.Name + " " + n.Description).Contains(h, StringComparison.OrdinalIgnoreCase)))
            .Select(n => (Nic: n, Props: n.GetIPProperties()))
            .Where(x => x.Props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .OrderBy(x => x.Nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1)
            .SelectMany(x => x.Props.UnicastAddresses)
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
    }

    /// <summary>Asks for admin rights once to let the phone reach Orange through Windows Firewall.</summary>
    public static bool OpenFirewall()
    {
        var port = Settings.Current.PhonePort;
        // Clicking away Windows' "allow this app?" prompt leaves BLOCK rules for Orange.exe, and block wins
        // over allow, so remove those first. Then allow only our port.
        var args = $"advfirewall firewall delete rule name=all dir=in program=\"{Environment.ProcessPath}\" & " +
                   $"netsh advfirewall firewall delete rule name=\"Orange phone sharing\" & " +
                   $"netsh advfirewall firewall add rule name=\"Orange phone sharing\" dir=in action=allow protocol=TCP localport={port} profile=private,public";
        try
        {
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c netsh {args}")
            {
                Verb = "runas", UseShellExecute = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            });
            p?.WaitForExit(15000);
            return p?.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // the user said no to the admin prompt
        }
    }

    private static string MimeOf(string name) => FileKinds.Ext(name) switch
    {
        "jpg" or "jpeg" => "image/jpeg", "png" => "image/png", "gif" => "image/gif", "webp" => "image/webp",
        "heic" => "image/heic", "mp4" or "m4v" => "video/mp4", "mov" => "video/quicktime", "mp3" => "audio/mpeg",
        "m4a" => "audio/mp4", "wav" => "audio/wav", "pdf" => "application/pdf", "txt" => "text/plain; charset=utf-8",
        "zip" => "application/zip",
        "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        _ => "application/octet-stream",
    };
}

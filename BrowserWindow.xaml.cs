using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ClaudeUsageWidget;

public partial class BrowserWindow : Window
{
    private Task _initTask;
    private CoreWebView2Environment? _env;
    private WebView2 WebView = null!;
    private TaskCompletionSource<bool>? _navigationTcs;
    private bool _loginActive;

    public event Action? LoginDone;

    public BrowserWindow()
    {
        InitializeComponent();
        if (!AppProfile.IsDefault) Title = $"Claude Usage ({AppProfile.Name}) - Sign in";
        _initTask = CreateWebViewAsync();
    }

    private async Task CreateWebViewAsync()
    {
        // Per-profile folder: WebView2 locks a user-data folder to a single
        // process, and it is what holds the claude.ai cookies, so two widgets with
        // two logins must not share it.
        var dataDir = AppProfile.WebView2DataDir;
        Directory.CreateDirectory(dataDir);

        _env = await CoreWebView2Environment.CreateAsync(userDataFolder: dataDir);
        var webView = new WebView2();
        WebViewHost.Child = webView;
        await webView.EnsureCoreWebView2Async(_env);

        webView.CoreWebView2.NavigationCompleted += (_, _) =>
        {
            _navigationTcs?.TrySetResult(true);
        };

        WebView = webView;
    }

    public async Task WaitUntilReadyAsync()
    {
        // A failed (re)creation is retried rather than leaving every later call
        // rethrowing the same stale exception.
        if (_initTask.IsFaulted) _initTask = CreateWebViewAsync();
        await _initTask;
    }

    // claude.ai is reloaded every refresh, but same-site navigations reuse one renderer
    // process and its memory only ever grows — left alone for a few days it reached
    // ~2 GB per widget. Between refreshes the page is unloaded, and once the renderer
    // is over this size anyway the whole WebView2 is thrown away and rebuilt, which
    // kills its processes. The login lives in on-disk cookies, so it survives that.
    private const long RecycleThresholdBytes = 350L * 1024 * 1024;

    public async Task ReleaseAsync(bool forceRecycle = false)
    {
        // Never pull the page out from under someone who is busy logging in.
        if (_loginActive) return;
        await WaitUntilReadyAsync();

        if (forceRecycle || RendererPrivateBytes() > RecycleThresholdBytes)
        {
            var previous = _initTask;
            _initTask = RecycleAsync(previous);
            await _initTask;
            return;
        }

        // Drops claude.ai's document (and its JS heap) until the next refresh.
        WebView.CoreWebView2.Navigate("about:blank");
    }

    private long RendererPrivateBytes()
    {
        if (_env == null) return 0;
        long total = 0;
        foreach (var info in _env.GetProcessInfos())
        {
            if (info.Kind != CoreWebView2ProcessKind.Renderer) continue;
            try
            {
                using var process = Process.GetProcessById(info.ProcessId);
                total += process.PrivateMemorySize64;
            }
            catch (ArgumentException) { } // exited since it was listed
            catch (InvalidOperationException) { }
        }
        return total;
    }

    private async Task RecycleAsync(Task previous)
    {
        try { await previous; } catch { } // a broken WebView is exactly what we're replacing

        var oldEnv = _env;
        var oldView = WebViewHost.Child as WebView2;
        if (oldView != null)
        {
            // Wait for the old browser process to exit so the new environment starts
            // fresh processes instead of attaching to the bloated ones.
            var exited = new TaskCompletionSource<bool>();
            if (oldEnv != null) oldEnv.BrowserProcessExited += (_, _) => exited.TrySetResult(true);
            WebViewHost.Child = null;
            oldView.Dispose();
            await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        }

        await CreateWebViewAsync();
    }

    private static readonly TimeSpan OpTimeout = TimeSpan.FromSeconds(30);

    // WebView2 events/calls occasionally never complete (stalled navigation, wedged
    // renderer process, etc.). Without a timeout, a single stuck refresh leaves the
    // caller's "_refreshing" flag set forever and every future refresh silently no-ops.
    private static async Task<T> WithTimeout<T>(Task<T> task, string what)
    {
        var completed = await Task.WhenAny(task, Task.Delay(OpTimeout));
        if (completed != task) throw new TimeoutException($"Timed out waiting for {what}.");
        return await task;
    }

    private static async Task WithTimeout(Task task, string what)
    {
        var completed = await Task.WhenAny(task, Task.Delay(OpTimeout));
        if (completed != task) throw new TimeoutException($"Timed out waiting for {what}.");
        await task;
    }

    public async Task NavigateAsync(string url)
    {
        await WaitUntilReadyAsync();
        _navigationTcs = new TaskCompletionSource<bool>();
        WebView.CoreWebView2.Navigate(url);
        await WithTimeout(_navigationTcs.Task, "page navigation");
        // Give the SPA a moment to fetch and render its data after the shell loads.
        await Task.Delay(2500);
    }

    public async Task ReloadAsync()
    {
        await WaitUntilReadyAsync();
        _navigationTcs = new TaskCompletionSource<bool>();
        WebView.CoreWebView2.Reload();
        await WithTimeout(_navigationTcs.Task, "page reload");
        await Task.Delay(2500);
    }

    // claude.ai's usage page lists its limits in order — the current session first, then
    // the weekly limit — each rendering a "Resets ..." line followed by a "NN% used"
    // figure. Pairing them up in the page rather than returning two parallel lists
    // matters: a section that omits its reset line would otherwise shift every later
    // reset onto the wrong percentage. The exact wording differs between account types,
    // so nothing here assumes a particular plan's labels.
    private const string ExtractScript = @"
(function() {
    var items = [];
    function walk(node) {
        if (node.nodeType === Node.TEXT_NODE) {
            var t = node.textContent.trim();
            var m = /^(\d{1,3})\s?%/.exec(t);
            if (m) { items.push({ t: 'p', v: m[1] }); return; }
            if (/^Resets\b\s+.+$/i.test(t)) items.push({ t: 'r', v: t });
        } else {
            for (var i = 0; i < node.childNodes.length; i++) walk(node.childNodes[i]);
        }
    }
    walk(document.body);

    var sections = [];
    var pending = null;
    for (var i = 0; i < items.length; i++) {
        if (items[i].t === 'r') {
            pending = items[i].v;
        } else {
            sections.push({ percent: items[i].v, reset: pending || '' });
            pending = null;
        }
    }
    return JSON.stringify(sections);
})();";

    public record Section(int Percent, string Reset);

    public record UsageSnapshot(Section[] Sections)
    {
        public static readonly UsageSnapshot Empty = new([]);

        public bool IsEmpty => Sections.Length == 0;

        // Index 0 is the current session, index 1 the weekly limit. A page showing only
        // one of them simply yields no value for the other.
        public int? Session => Sections.Length > 0 ? Sections[0].Percent : null;
        public int? Weekly => Sections.Length > 1 ? Sections[1].Percent : null;
        public string SessionReset => Sections.Length > 0 ? Sections[0].Reset : "";
        public string WeeklyReset => Sections.Length > 1 ? Sections[1].Reset : "";
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<UsageSnapshot> ExtractUsageAsync()
    {
        await WaitUntilReadyAsync();
        var json = await WithTimeout(WebView.CoreWebView2.ExecuteScriptAsync(ExtractScript), "usage extraction");
        // ExecuteScriptAsync returns a JSON-encoded string; the script itself already
        // returns a JSON string, so unwrap once.
        var inner = JsonSerializer.Deserialize<string>(json) ?? "";
        if (string.IsNullOrEmpty(inner)) return UsageSnapshot.Empty;

        var raw = JsonSerializer.Deserialize<RawSection[]>(inner, JsonOptions);
        if (raw == null) return UsageSnapshot.Empty;

        var sections = new List<Section>();
        foreach (var r in raw)
        {
            if (int.TryParse(r.Percent, out var value)) sections.Add(new Section(value, r.Reset ?? ""));
        }
        return new UsageSnapshot([.. sections]);
    }

    private sealed class RawSection
    {
        public string? Percent { get; set; }
        public string? Reset { get; set; }
    }

    public void ShowForLogin(string url)
    {
        _loginActive = true;
        Left = 100;
        Top = 100;
        Show();
        Activate();
        _ = NavigateAsync(url);
    }

    public void HideOffscreen()
    {
        _loginActive = false;
        Left = -4000;
        Top = -4000;
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        HideOffscreen();
        LoginDone?.Invoke();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Never actually close from the window chrome; just hide.
        e.Cancel = true;
        HideOffscreen();
    }
}

using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace ClaudeUsageWidget;

public partial class BrowserWindow : Window
{
    private bool _ready;
    private TaskCompletionSource<bool>? _navigationTcs;

    public event Action? LoginDone;

    public BrowserWindow()
    {
        InitializeComponent();
        if (!AppProfile.IsDefault) Title = $"Claude Usage ({AppProfile.Name}) - Sign in";
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        // Per-profile folder: WebView2 locks a user-data folder to a single
        // process, and it is what holds the claude.ai cookies, so two widgets with
        // two logins must not share it.
        var dataDir = AppProfile.WebView2DataDir;
        Directory.CreateDirectory(dataDir);

        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: dataDir);
        await WebView.EnsureCoreWebView2Async(env);

        WebView.CoreWebView2.NavigationCompleted += (_, _) =>
        {
            _navigationTcs?.TrySetResult(true);
        };

        _ready = true;
    }

    public async Task WaitUntilReadyAsync()
    {
        while (!_ready) await Task.Delay(50);
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
        Left = 100;
        Top = 100;
        Show();
        Activate();
        _ = NavigateAsync(url);
    }

    public void HideOffscreen()
    {
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

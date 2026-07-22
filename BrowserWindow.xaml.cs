using System;
using System.IO;
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
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClaudeUsageWidget", "WebView2Data");
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

    // Looks for a text node that starts with "NN%" (claude.ai renders usage as
    // e.g. "44% used" in a single text node, not an isolated "44%").
    private const string ExtractScript = @"
(function() {
    function walk(node, results) {
        if (node.nodeType === Node.TEXT_NODE) {
            var t = node.textContent.trim();
            var m = /^(\d{1,3})\s?%/.exec(t);
            if (m) {
                results.push(m[1] + ""%"");
            }
        } else {
            for (var i = 0; i < node.childNodes.length; i++) {
                walk(node.childNodes[i], results);
            }
        }
        return results;
    }
    return JSON.stringify(walk(document.body, []));
})();";

    public async Task<string[]> ExtractPercentagesAsync()
    {
        await WaitUntilReadyAsync();
        var json = await WithTimeout(WebView.CoreWebView2.ExecuteScriptAsync(ExtractScript), "percentage extraction");
        // ExecuteScriptAsync returns a JSON-encoded string; the script itself already
        // returns a JSON string, so unwrap once.
        var inner = JsonSerializer.Deserialize<string>(json) ?? "[]";
        return JsonSerializer.Deserialize<string[]>(inner) ?? Array.Empty<string>();
    }

    // Finds the first "Resets in X hr Y min" text node (the current-session countdown)
    // and returns the part after "Resets in ", e.g. "2 hr 31 min".
    private const string ResetScript = @"
(function() {
    function walk(node) {
        if (node.nodeType === Node.TEXT_NODE) {
            var t = node.textContent.trim();
            var m = /^Resets in\s+(.+)$/i.exec(t);
            if (m) return m[1];
        } else {
            for (var i = 0; i < node.childNodes.length; i++) {
                var r = walk(node.childNodes[i]);
                if (r) return r;
            }
        }
        return null;
    }
    return walk(document.body) || """";
})();";

    public async Task<string> ExtractResetInTextAsync()
    {
        await WaitUntilReadyAsync();
        var json = await WithTimeout(WebView.CoreWebView2.ExecuteScriptAsync(ResetScript), "reset-time extraction");
        return JsonSerializer.Deserialize<string>(json) ?? "";
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

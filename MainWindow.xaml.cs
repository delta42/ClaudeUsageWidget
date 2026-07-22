using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ClaudeUsageWidget;

public partial class MainWindow : Window
{
    private readonly Settings _settings;
    private readonly BrowserWindow _browser;
    private readonly DispatcherTimer _timer;
    private bool _refreshing;

    public MainWindow()
    {
        InitializeComponent();

        _settings = Settings.Load();
        Left = _settings.WindowLeft;
        Top = _settings.WindowTop;

        _browser = new BrowserWindow();
        _browser.LoginDone += () => _ = RefreshAsync();
        _browser.Show(); // rendered off-screen so background navigation/scripting keeps working
        _browser.HideOffscreen();

        _timer = new DispatcherTimer();
        ApplyRefreshInterval();
        _timer.Tick += (_, _) => _ = RefreshAsync();
        _timer.Start();

        Loaded += async (_, _) => await RefreshAsync();
        Closing += (_, _) =>
        {
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
            _settings.Save();
        };
    }

    private void ApplyRefreshInterval()
    {
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(5, _settings.RefreshSeconds));
    }

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        StartIconFlash();
        try
        {
            ValueText.ToolTip = null;
            await _browser.NavigateAsync(_settings.Url);

            // The SPA can take a little longer than usual to render its data on a
            // slow load; retry briefly instead of giving up after a single attempt.
            var matches = Array.Empty<string>();
            for (var attempt = 0; attempt < 6 && matches.Length == 0; attempt++)
            {
                if (attempt > 0) await System.Threading.Tasks.Task.Delay(1000);
                matches = await _browser.ExtractPercentagesAsync();
            }
            ValueText.Text = matches.Length > 0 ? matches[0].Replace(" ", "") : "?";
            if (matches.Length == 0)
                ValueText.ToolTip = "No \"NN%\" text found on the page. Are you logged in?";

            var resetIn = await _browser.ExtractResetInTextAsync();
            ResetText.Text = FormatResetTime(resetIn);
        }
        catch (Exception ex)
        {
            ValueText.Text = "err";
            ValueText.ToolTip = ex.Message;
        }
        finally
        {
            _refreshing = false;
            StopIconFlash();
        }
    }

    private void StartIconFlash()
    {
        var animation = new DoubleAnimation(1.0, 0.25, TimeSpan.FromMilliseconds(600))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        IconImage.BeginAnimation(OpacityProperty, animation);
    }

    private void StopIconFlash()
    {
        IconImage.BeginAnimation(OpacityProperty, null);
        IconImage.Opacity = 1.0;
    }

    // Parses a countdown like "2 hr 31 min" or "45 min" and returns the clock
    // time it resolves to, formatted per the OS's regional time settings.
    private static string FormatResetTime(string resetIn)
    {
        if (string.IsNullOrWhiteSpace(resetIn)) return "";

        var hourMatch = Regex.Match(resetIn, @"(\d+)\s*hr", RegexOptions.IgnoreCase);
        var minMatch = Regex.Match(resetIn, @"(\d+)\s*min", RegexOptions.IgnoreCase);
        if (!hourMatch.Success && !minMatch.Success) return "";

        var hours = hourMatch.Success ? int.Parse(hourMatch.Groups[1].Value) : 0;
        var minutes = minMatch.Success ? int.Parse(minMatch.Groups[1].Value) : 0;
        var resetAt = DateTime.Now.Add(new TimeSpan(hours, minutes, 0));
        return $"Resets at {resetAt.ToShortTimeString()}";
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void LogIn_Click(object sender, RoutedEventArgs e)
    {
        _browser.ShowForLogin(_settings.Url);
    }

    private void RefreshNow_Click(object sender, RoutedEventArgs e)
    {
        _ = RefreshAsync();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsWindow(_settings) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _settings.Save();
            ApplyRefreshInterval();
            _ = RefreshAsync();
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
}

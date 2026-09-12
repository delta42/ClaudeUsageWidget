using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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

        if (!AppProfile.IsDefault)
        {
            // The accent colour is what tells instances apart on screen; the name
            // itself only needs to be reachable, not permanently on display.
            Title = $"Claude Usage - {AppProfile.Name}";
        }
        AccentBorder.ToolTip = DefaultToolTip;
        ApplyAccent();

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

    // Paints the profile's accent as a stripe down the widget's left edge; the
    // translucent body then picks up a wash of the same colour. No accent (the plain
    // default profile) restores the original, stripe-free look.
    private void ApplyAccent()
    {
        var accent = AccentPalette.Resolve(_settings.AccentColor, AppProfile.Name);
        if (accent == null)
        {
            AccentBorder.Background = Brushes.Transparent;
            AccentBorder.Padding = new Thickness(0);
            return;
        }

        AccentBorder.Background = new SolidColorBrush(accent.Value);
        AccentBorder.Padding = new Thickness(AccentStripeWidth, 0, 0, 0);
    }

    private const double AccentStripeWidth = 5;

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
            var usage = BrowserWindow.UsageSnapshot.Empty;
            for (var attempt = 0; attempt < 6 && usage.IsEmpty; attempt++)
            {
                if (attempt > 0) await System.Threading.Tasks.Task.Delay(1000);
                usage = await _browser.ExtractUsageAsync();
            }

            ValueText.Text = usage.Session is int session ? $"{session}%" : "?";
            if (usage.IsEmpty)
                ValueText.ToolTip = "No \"NN%\" text found on the page. Are you logged in?";

            ResetText.Text = FormatResetClock(usage.SessionReset);
            ShowWeekly(usage);
            ApplyBlockedState(usage);
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

    // The session percentage alone can read reassuringly low while the weekly limit is
    // what's actually blocking you, so the weekly figure gets its own line — and turns
    // amber, then red, as it approaches the point where you can't use the account.
    private void ShowWeekly(BrowserWindow.UsageSnapshot usage)
    {
        if (usage.Weekly is not int weekly)
        {
            WeeklyText.Visibility = Visibility.Collapsed;
            return;
        }

        var countdown = FormatResetCountdown(usage.WeeklyReset);
        WeeklyText.Text = countdown.Length > 0 ? $"Week {weekly}% · {countdown}" : $"Week {weekly}%";
        WeeklyText.Foreground = new SolidColorBrush(WeeklyColour(weekly));
        WeeklyText.Visibility = Visibility.Visible;
    }

    // Either limit hitting 100% means the account can't be used until it resets, which
    // is the one state worth interrupting a glance for: the widget goes red and a slash
    // is drawn across it.
    private void ApplyBlockedState(BrowserWindow.UsageSnapshot usage)
    {
        var blocked = usage.Session >= 100 || usage.Weekly >= 100;
        BlockedSlash.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;

        if (blocked)
        {
            var exhausted = new SolidColorBrush(ExhaustedColour);
            ValueText.Foreground = exhausted;
            AccentBorder.Background = exhausted;
            AccentBorder.Padding = new Thickness(AccentStripeWidth, 0, 0, 0);
            AccentBorder.ToolTip = usage.Weekly >= 100
                ? "Weekly limit reached — you can't use this account until it resets."
                : "Session limit reached — you can't use this account until it resets.";
            return;
        }

        ValueText.Foreground = Brushes.White;
        ApplyAccent();
        AccentBorder.ToolTip = DefaultToolTip;
    }

    private string DefaultToolTip => AppProfile.IsDefault
        ? "Claude session usage — right-click for options"
        : $"Claude session usage ({AppProfile.Name}) — right-click for options";

    private static readonly Color ExhaustedColour = Color.FromRgb(0xE8, 0x65, 0x5F);

    private static Color WeeklyColour(int weekly) => weekly switch
    {
        >= 100 => ExhaustedColour,
        >= 85 => Color.FromRgb(0xE8, 0xA3, 0x3D),
        _ => Color.FromRgb(0xBB, 0xBB, 0xBB)
    };

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

    // claude.ai words a reset in one of three ways, depending on the account and on
    // whether it is the session or the weekly limit:
    //   "Resets in 50 min"          — a countdown
    //   "Resets at 8:50 PM"         — a time later today
    //   "Resets Saturday 8:00 AM"   — a named weekday, spelled out or abbreviated
    //   "Resets Sun 2:00 AM"          (the same page can use both forms at once)
    // All three are resolved to an actual moment here, so the widget can render them
    // in one consistent style rather than echoing whichever wording the page used.
    private static DateTime? ParseResetMoment(string resetText)
    {
        if (string.IsNullOrWhiteSpace(resetText)) return null;
        var now = DateTime.Now;

        var countdown = Regex.Match(resetText, @"^Resets\s+in\s+(.+)$", RegexOptions.IgnoreCase);
        if (countdown.Success)
        {
            var remaining = countdown.Groups[1].Value;
            var days = ReadUnit(remaining, "day");
            var hours = ReadUnit(remaining, "hr|hour");
            var minutes = ReadUnit(remaining, "min");
            if (days == 0 && hours == 0 && minutes == 0) return null;
            return now.Add(new TimeSpan(days, hours, minutes, 0));
        }

        // "Resets at 8:50 PM" — later today, or the same time tomorrow if it has passed.
        var atTime = Regex.Match(resetText, @"^Resets\s+at\s+(.+)$", RegexOptions.IgnoreCase);
        if (atTime.Success)
        {
            if (!TryParseTimeOfDay(atTime.Groups[1].Value, out var time)) return null;
            var moment = now.Date + time;
            return moment > now ? moment : moment.AddDays(1);
        }

        // "Resets Saturday 8:00 AM" / "Resets Sun 2:00 AM" — the next occurrence of that weekday.
        var weekday = Regex.Match(
            resetText,
            @"^Resets\s+(Mon|Tue|Wed|Thu|Fri|Sat|Sun)[a-z]*\.?\s*,?\s*(?:at\s+)?(.*)$",
            RegexOptions.IgnoreCase);
        if (weekday.Success && TryParseWeekday(weekday.Groups[1].Value, out var day))
        {
            TryParseTimeOfDay(weekday.Groups[2].Value, out var time);
            var ahead = ((int)day - (int)now.DayOfWeek + 7) % 7;
            var moment = now.Date.AddDays(ahead) + time;
            return moment > now ? moment : moment.AddDays(7);
        }

        return null;
    }

    private static int ReadUnit(string text, string unitPattern)
    {
        var m = Regex.Match(text, $@"(\d+)\s*({unitPattern})", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    // Keyed on the first three letters, which is all the regex above captures.
    private static bool TryParseWeekday(string prefix, out DayOfWeek day)
    {
        foreach (var candidate in Enum.GetValues<DayOfWeek>())
        {
            if (candidate.ToString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                day = candidate;
                return true;
            }
        }
        day = default;
        return false;
    }

    private static bool TryParseTimeOfDay(string text, out TimeSpan time)
    {
        time = default;
        text = text.Trim();
        if (text.Length == 0) return false;

        // The page's times are English ("2:00 AM"), so try the invariant culture first:
        // a 24-hour OS locale may otherwise read the time while dropping the AM/PM.
        foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.CurrentCulture })
        {
            if (DateTime.TryParse(text, culture, DateTimeStyles.NoCurrentDateDefault, out var parsed))
            {
                time = parsed.TimeOfDay;
                return true;
            }
        }
        return false;
    }

    // The session line reads as a clock time — "Resets at 17:30" — in whatever format
    // the OS's regional settings use.
    private static string FormatResetClock(string resetText)
    {
        var moment = ParseResetMoment(resetText);
        return moment == null ? "" : $"Resets at {moment.Value.ToShortTimeString()}";
    }

    // The weekly line reads as a countdown — "in 2 days, 15 hr" — because a weekly
    // reset is usually far enough out that a bare day and time makes you do the
    // arithmetic yourself.
    private static string FormatResetCountdown(string resetText)
    {
        var moment = ParseResetMoment(resetText);
        if (moment == null) return "";

        var left = moment.Value - DateTime.Now;
        if (left <= TimeSpan.Zero) return "now";
        // Compact units — this line sets the widget's width, and the widget is meant to
        // sit in a corner of the screen rather than announce itself.
        if (left.TotalHours < 1) return $"in {left.Minutes}m";
        if (left.TotalDays < 1) return left.Minutes > 0 ? $"in {left.Hours}h {left.Minutes}m" : $"in {left.Hours}h";
        return left.Hours > 0 ? $"in {left.Days}d {left.Hours}h" : $"in {left.Days}d";
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
            ApplyAccent();
            _ = RefreshAsync();
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }
}

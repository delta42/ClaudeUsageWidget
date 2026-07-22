using System.Windows;

namespace ClaudeUsageWidget;

public partial class SettingsWindow : Window
{
    private readonly Settings _settings;

    public SettingsWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;
        UrlBox.Text = settings.Url;
        RefreshBox.Text = settings.RefreshSeconds.ToString();
        LaunchAtStartupBox.IsChecked = settings.LaunchAtStartup;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.Url = UrlBox.Text.Trim();
        if (int.TryParse(RefreshBox.Text.Trim(), out var seconds) && seconds > 0)
        {
            _settings.RefreshSeconds = seconds;
        }

        _settings.LaunchAtStartup = LaunchAtStartupBox.IsChecked == true;
        StartupManager.SetEnabled(_settings.LaunchAtStartup);

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

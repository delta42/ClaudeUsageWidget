using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClaudeUsageWidget;

public partial class SettingsWindow : Window
{
    private readonly Settings _settings;

    public SettingsWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;
        Title = AppProfile.IsDefault ? "Widget Settings" : $"Widget Settings - {AppProfile.Name}";
        ProfileLabel.Text = $"Profile: {AppProfile.Name}  (own login and settings)";
        UrlBox.Text = settings.Url;
        RefreshBox.Text = settings.RefreshSeconds.ToString();
        PopulateAccents(settings.AccentColor);
        LaunchAtStartupBox.IsChecked = settings.LaunchAtStartup;
    }

    // "Automatic" (Tag = "") derives the colour from the profile name; the rest are
    // explicit overrides. Each item shows a swatch so the choice is visible here too.
    private void PopulateAccents(string configured)
    {
        AccentBox.Items.Add(new ComboBoxItem { Content = "Automatic (from profile name)", Tag = "" });
        foreach (var choice in AccentPalette.Named)
        {
            var swatch = new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(choice.Hex)!),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(swatch);
            row.Children.Add(new TextBlock { Text = choice.Name, VerticalAlignment = VerticalAlignment.Center });
            AccentBox.Items.Add(new ComboBoxItem { Content = row, Tag = choice.Hex });
        }

        AccentBox.SelectedIndex = Math.Max(0, AccentBox.Items
            .Cast<ComboBoxItem>()
            .ToList()
            .FindIndex(i => string.Equals((string)i.Tag, configured, StringComparison.OrdinalIgnoreCase)));
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.Url = UrlBox.Text.Trim();
        if (int.TryParse(RefreshBox.Text.Trim(), out var seconds) && seconds > 0)
        {
            _settings.RefreshSeconds = seconds;
        }

        _settings.AccentColor = (string)((ComboBoxItem)AccentBox.SelectedItem).Tag;

        _settings.LaunchAtStartup = LaunchAtStartupBox.IsChecked == true;
        StartupManager.SetEnabled(_settings.LaunchAtStartup);

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}

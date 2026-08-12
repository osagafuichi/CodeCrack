using System.Windows;
using System.Windows.Controls;
using CodeCrack.App.Core.Editor;
using CodeCrack.App.Core.Settings;
using CodeCrackApp.Services;

namespace CodeCrackApp.Settings;

public partial class SettingsWindow : Window
{
    private readonly IAppSettings _settings;

    public SettingsWindow(IAppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        FontSlider.Value = settings.FontSize;
        IndentCombo.SelectedIndex = settings.IndentWidth switch { 2 => 0, 8 => 2, _ => 1 };
        SpacesCheck.IsChecked = settings.IndentUsesSpaces;
        ThemeCombo.ItemsSource = EditorThemeRegistry.All;
        ThemeCombo.SelectedValue = settings.EditorTheme;
        EngineBox.Text = settings.EnginePathOverride;
        ApiKeyBox.Password = Dpapi.Unprotect(settings.ClaudeApiKey); // show decrypted (as dots)
    }

    private void OnDone(object sender, RoutedEventArgs e)
    {
        _settings.FontSize = (int)FontSlider.Value;
        _settings.IndentWidth = ((ComboBoxItem)IndentCombo.SelectedItem).Content is "2" ? 2
                              : ((ComboBoxItem)IndentCombo.SelectedItem).Content is "8" ? 8 : 4;
        _settings.IndentUsesSpaces = SpacesCheck.IsChecked == true;
        if (ThemeCombo.SelectedValue is string themeKey) _settings.EditorTheme = themeKey;
        _settings.EnginePathOverride = EngineBox.Text;
        _settings.ClaudeApiKey = Dpapi.Protect(ApiKeyBox.Password); // encrypt before persisting
        _settings.Save();
        Close();
    }
}

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using DanteCLI.ViewModels;

namespace DanteCLI.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        var s = AppState.Shared.Settings;
        ApiKeyBox.Password = s.GroqApiKey ?? "";
        LangBox.Text = string.IsNullOrEmpty(s.VoiceLanguage) ? "pt" : s.VoiceLanguage;
        AutoSubmitBox.IsChecked = s.VoiceAutoSubmit;
        foreach (ComboBoxItem item in ModelBox.Items)
        {
            if ((string)item.Tag == s.VoiceModel)
            {
                ModelBox.SelectedItem = item;
                break;
            }
        }
        if (ModelBox.SelectedItem is null) ModelBox.SelectedIndex = 0;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = AppState.Shared.Settings;
        s.GroqApiKey = ApiKeyBox.Password ?? "";
        s.VoiceLanguage = LangBox.Text?.Trim() ?? "pt";
        s.VoiceAutoSubmit = AutoSubmitBox.IsChecked == true;
        if (ModelBox.SelectedItem is ComboBoxItem item && item.Tag is string m)
            s.VoiceModel = m;
        AppState.Shared.Settings = s; // triggers save
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch { }
        e.Handled = true;
    }
}

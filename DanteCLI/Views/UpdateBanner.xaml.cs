using System;
using System.Windows;
using System.Windows.Controls;
using DanteCLI.Services;

namespace DanteCLI.Views;

public partial class UpdateBanner : UserControl
{
    public event EventHandler? Dismissed;
    private UpdateManifest? _manifest;

    public UpdateBanner()
    {
        InitializeComponent();
        UpdateChecker.Shared.StateChanged += (_, _) =>
            Dispatcher.BeginInvoke(new Action(Sync));
    }

    public void Bind(UpdateManifest manifest)
    {
        _manifest = manifest;
        TitleText.Text = $"Atualização disponível: v{manifest.Version}";
        NotesText.Text = manifest.ReleaseNotes ?? "";
        NotesText.Visibility = string.IsNullOrEmpty(manifest.ReleaseNotes)
            ? Visibility.Collapsed : Visibility.Visible;
        Sync();
    }

    private void Sync()
    {
        var c = UpdateChecker.Shared;
        if (c.IsDownloading)
        {
            InstallBtnText.Text = "Baixando…";
            InstallBtn.IsEnabled = false;
            Progress.Value = c.DownloadProgress * 100;
            Progress.Visibility = Visibility.Visible;
        }
        else
        {
            InstallBtnText.Text = "Baixar e instalar";
            InstallBtn.IsEnabled = true;
            Progress.Visibility = Visibility.Collapsed;
        }
    }

    private async void InstallBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_manifest is null) return;
        var (ok, msg) = await UpdateChecker.Shared.DownloadAndInstallAsync(_manifest);
        if (ok)
        {
            MessageBox.Show(Window.GetWindow(this),
                "EXE baixado. Feche o app, rode o novo .exe da pasta Downloads.",
                "Atualização", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(Window.GetWindow(this),
                "Falha: " + msg, "Atualização",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e) =>
        Dismissed?.Invoke(this, EventArgs.Empty);
}

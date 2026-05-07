using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DanteCLI.Services;
using DanteCLI.ViewModels;

namespace DanteCLI.Views;

public partial class MicButton : UserControl
{
    private enum Status { Idle, Recording, Transcribing }

    private readonly AudioRecorder _recorder = new();
    private Status _status = Status.Idle;
    private string? _currentFile;

    public MicButton()
    {
        InitializeComponent();
        Render();
    }

    private void Render()
    {
        switch (_status)
        {
            case Status.Idle:
                Icon.Visibility = Visibility.Visible;
                RecDot.Visibility = Visibility.Collapsed;
                StopGlyph.Visibility = Visibility.Collapsed;
                Spinner.Visibility = Visibility.Collapsed;
                Label.Text = "Voz";
                Btn.ToolTip = string.IsNullOrEmpty(AppState.Shared.Settings.GroqApiKey)
                    ? "Configure API key do Groq em Settings → Voz"
                    : "Gravar áudio (transcreve via Groq Whisper e injeta no terminal)";
                break;
            case Status.Recording:
                Icon.Visibility = Visibility.Collapsed;
                RecDot.Visibility = Visibility.Visible;
                StopGlyph.Visibility = Visibility.Visible;
                Spinner.Visibility = Visibility.Collapsed;
                Label.Text = "Parar";
                Btn.ToolTip = "Parar gravação e transcrever";
                break;
            case Status.Transcribing:
                Icon.Visibility = Visibility.Collapsed;
                RecDot.Visibility = Visibility.Collapsed;
                StopGlyph.Visibility = Visibility.Collapsed;
                Spinner.Visibility = Visibility.Visible;
                Label.Text = "Transcrevendo…";
                Btn.ToolTip = "Aguarde";
                break;
        }
    }

    private async void Btn_Click(object sender, RoutedEventArgs e)
    {
        switch (_status)
        {
            case Status.Idle:
                if (string.IsNullOrEmpty(AppState.Shared.Settings.GroqApiKey))
                {
                    MessageBox.Show(Window.GetWindow(this),
                        "Configure a API key do Groq em Settings → Voz.", "Voz",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                _currentFile = _recorder.Start();
                if (_currentFile is null)
                {
                    var msg = _recorder.LastDeviceMissing
                        ? "Nenhum microfone detectado."
                        : "Não foi possível iniciar a gravação.";
                    MessageBox.Show(Window.GetWindow(this), msg, "Voz",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _status = Status.Recording;
                Render();
                break;

            case Status.Recording:
                var path = _recorder.Stop();
                _status = Status.Transcribing;
                Render();
                // Wait briefly for the writer to flush via NAudio's RecordingStopped.
                await Task.Delay(150);
                await TranscribeAsync(path);
                break;

            case Status.Transcribing:
                break;
        }
    }

    private async Task TranscribeAsync(string? path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            var info = new FileInfo(path);
            if (info.Length < 8_000)
            {
                MessageBox.Show(Window.GetWindow(this),
                    "Gravação muito curta. Segure o botão por pelo menos 0,5s falando.",
                    "Voz", MessageBoxButton.OK, MessageBoxImage.Information);
                File.Delete(path);
                return;
            }

            var s = AppState.Shared.Settings;
            var client = new GroqWhisperClient(s.GroqApiKey, s.VoiceModel, s.VoiceLanguage);
            var text = await client.TranscribeAsync(path);
            text = text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(text))
            {
                if (s.VoiceAutoSubmit) text += "\r\n";
                AppState.Shared.RaiseInjectIntoActiveTerminal(text);
            }
            try { File.Delete(path); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this),
                "Falha na transcrição: " + ex.Message,
                "Voz", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _status = Status.Idle;
            Render();
        }
    }
}

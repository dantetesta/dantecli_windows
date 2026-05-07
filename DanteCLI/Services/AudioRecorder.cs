using System;
using System.IO;
using NAudio.Wave;

namespace DanteCLI.Services;

/// <summary>
/// Captures microphone audio to a temp WAV file (16kHz mono Int16) using NAudio.
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _outPath;

    public bool IsRecording { get; private set; }
    public bool LastDeviceMissing { get; private set; }

    public string? Start()
    {
        if (IsRecording) return _outPath;
        LastDeviceMissing = false;

        if (WaveInEvent.DeviceCount == 0)
        {
            LastDeviceMissing = true;
            return null;
        }

        var path = Path.Combine(Path.GetTempPath(), $"dante-mic-{Guid.NewGuid()}.wav");
        var format = new WaveFormat(16000, 16, 1);

        try
        {
            _writer = new WaveFileWriter(path, format);
            _waveIn = new WaveInEvent { WaveFormat = format, BufferMilliseconds = 50 };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
            _waveIn.StartRecording();
            IsRecording = true;
            _outPath = path;
            return path;
        }
        catch
        {
            try { _writer?.Dispose(); } catch { }
            try { _waveIn?.Dispose(); } catch { }
            _writer = null;
            _waveIn = null;
            return null;
        }
    }

    public string? Stop()
    {
        if (!IsRecording) return _outPath;
        try { _waveIn?.StopRecording(); } catch { }
        // Disposal happens in OnRecordingStopped
        return _outPath;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try { _writer?.Write(e.Buffer, 0, e.BytesRecorded); } catch { }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        try { _writer?.Dispose(); } catch { }
        try { _waveIn?.Dispose(); } catch { }
        _writer = null;
        _waveIn = null;
        IsRecording = false;
    }

    public void Dispose()
    {
        try { _waveIn?.StopRecording(); } catch { }
        try { _waveIn?.Dispose(); } catch { }
        try { _writer?.Dispose(); } catch { }
    }
}

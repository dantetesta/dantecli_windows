using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace DanteCLI.Services;

public sealed class GroqWhisperClient
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };

    private readonly string _apiKey;
    private readonly string _model;
    private readonly string? _language;

    public GroqWhisperClient(string apiKey, string model, string? language)
    {
        _apiKey = apiKey;
        _model = model;
        _language = string.IsNullOrEmpty(language) ? null : language;
    }

    public async Task<string> TranscribeAsync(string audioFilePath)
    {
        if (string.IsNullOrEmpty(_apiKey))
            throw new InvalidOperationException("API key do Groq não configurada (Settings → Voz).");

        using var req = new HttpRequestMessage(HttpMethod.Post,
            "https://api.groq.com/openai/v1/audio/transcriptions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(_model), "model");
        if (_language is not null)
            content.Add(new StringContent(_language), "language");
        content.Add(new StringContent("json"), "response_format");

        var fs = File.OpenRead(audioFilePath);
        var fileContent = new StreamContent(fs);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "file", Path.GetFileName(audioFilePath));

        req.Content = content;

        using var res = await _http.SendAsync(req).ConfigureAwait(false);
        var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)res.StatusCode}: {Trim(body, 300)}");

        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("text").GetString() ?? "";
        }
        catch
        {
            throw new InvalidOperationException("Resposta inválida do servidor.");
        }
    }

    private static string Trim(string s, int len) => s.Length > len ? s[..len] + "..." : s;
}

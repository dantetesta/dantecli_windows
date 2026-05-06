using System;
using System.IO;
using System.Text.Json;

namespace DanteCLI.Services;

public static class PersistenceRoot
{
    public static string Directory
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "DanteCLI");
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string PathFor(string fileName) => Path.Combine(Directory, fileName);

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}

public abstract class JsonStore<T> where T : class, new()
{
    protected abstract string FileName { get; }
    protected virtual T Fallback() => new();

    public T Load()
    {
        var path = PersistenceRoot.PathFor(FileName);
        if (!File.Exists(path)) return Fallback();
        try
        {
            using var fs = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(fs, PersistenceRoot.Json) ?? Fallback();
        }
        catch
        {
            return Fallback();
        }
    }

    public void Save(T value)
    {
        var path = PersistenceRoot.PathFor(FileName);
        try
        {
            var tmp = path + ".tmp";
            using (var fs = File.Create(tmp))
            {
                JsonSerializer.Serialize(fs, value, PersistenceRoot.Json);
            }
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            // best effort — ignore IO failures
        }
    }
}

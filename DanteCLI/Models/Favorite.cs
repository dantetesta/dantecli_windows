using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DanteCLI.Models;

public sealed class Favorite
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public List<string> Tags { get; set; } = new();
    public string ColorHex { get; set; } = TabColors.Blue;
    public string? Emoji { get; set; }
    public string? InitialCommand { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public string DisplayPath
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.StartsWith(home, StringComparison.OrdinalIgnoreCase)
                ? "~" + Path[home.Length..]
                : Path;
        }
    }
}

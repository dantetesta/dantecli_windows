using System;
using System.Collections.Generic;
using DanteCLI.Models;

namespace DanteCLI.Services;

public sealed class FavoritesStore : JsonStore<List<Favorite>>
{
    protected override string FileName => "favorites.json";

    protected override List<Favorite> Fallback() => new()
    {
        new Favorite { Name = "Home", Path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                       ColorHex = TabColors.Neutral, Emoji = "🏠" },
        new Favorite { Name = "Desktop", Path = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                       ColorHex = TabColors.Blue, Emoji = "🖥️" }
    };
}

public sealed class SettingsStore : JsonStore<AppSettings>
{
    protected override string FileName => "settings.json";
}

public sealed class AIProviderStore : JsonStore<List<AIProvider>>
{
    protected override string FileName => "ai_providers.json";
    protected override List<AIProvider> Fallback() => AIProvider.Defaults();
}

# DANTE CLI (Windows)

Terminal nativo Windows com tabs custom, favoritos com tags e quick-launch de AI CLIs.
Versão Windows do projeto [DANTE CLI](../) (macOS).

**Stack:** C# 12 · .NET 8 · WinUI 3 (Windows App SDK) · ConPTY via [Pty.Net](https://github.com/microsoft/Pty.Net)

## Pré-requisitos (build local)

- Windows 10 1809+ ou Windows 11
- [Visual Studio 2022](https://visualstudio.microsoft.com/downloads/) Community (gratuito) com workloads:
  - "Desenvolvimento .NET para área de trabalho"
  - "Desenvolvimento de aplicativos da Plataforma Universal do Windows"
  - "Desenvolvimento Windows App SDK C#"
- .NET 8 SDK
- Windows App SDK 1.6+

## Buildar localmente

```powershell
# clonar
git clone https://github.com/dantetesta/dantecli_windows.git
cd dantecli_windows

# restaurar dependências
dotnet restore

# rodar
dotnet run --project DanteCLI -c Debug
```

Ou abra `DanteCLI.sln` no Visual Studio e pressione **F5**.

## Build automatizado (GitHub Actions)

Toda push pra `main`/`master` ou tag `v*` dispara `.github/workflows/build.yml`:
- Compila Release x64 num runner `windows-latest`
- Faz `dotnet publish` self-contained
- Empacota num `DanteCLI-x64.zip`
- Em tags `v*`: cria GitHub Release com o ZIP

Pra cortar uma release:
```bash
git tag v0.1.0
git push origin v0.1.0
```

Após o workflow terminar (~3 min), o ZIP fica disponível em **Releases**.

## Estrutura

```
DanteCLI.sln
DanteCLI/
├── DanteCLI.csproj
├── App.xaml(.cs)              entry point WinUI
├── MainWindow.xaml(.cs)       layout principal
├── Package.appxmanifest       manifest MSIX (futuro)
├── app.manifest               manifest Win32
├── Models/                    Favorite, TerminalTab, AppSettings, AIProvider, TabColors
├── Services/                  PersistenceRoot (JSON), Stores, PtySession (ConPTY)
├── ViewModels/                AppState (singleton MVVM)
├── Views/
│   ├── TerminalView.xaml(.cs) RichTextBlock + ConPTY wrapper
│   ├── TabChip.xaml(.cs)      tab custom com cor/emoji
│   ├── FavoritesSidebar.xaml  busca + lista
│   └── FilesSidebar.xaml      tree view
└── Assets/                    icone-app.png + StoreLogo etc
```

Persistência em `%APPDATA%\DanteCLI\`:
- `favorites.json`
- `settings.json`
- `ai_providers.json`

**Schema compatível com a versão Mac** — você pode copiar os JSONs entre as duas plataformas.

## Status atual

✅ Scaffold completo
✅ Models + persistência
✅ TabBar custom
✅ Sidebar Favoritos / Arquivos
✅ ConPTY (spawn de pwsh/cmd)
⏳ ANSI parser completo (hoje só strip de escapes)
⏳ Editor com syntax highlighting (planejado: AvalonEdit-WinUI)
⏳ Split panes
⏳ AI quick-launch toolbar
⏳ Settings UI

## Atalhos planejados

| Ação                  | Atalho           |
|-----------------------|------------------|
| Nova aba              | Ctrl+T           |
| Fechar aba            | Ctrl+W           |
| Próxima aba           | Ctrl+Tab         |
| Buscar favoritos      | Ctrl+L           |
| Settings              | Ctrl+,           |

## Dependências NuGet

- **Microsoft.WindowsAppSDK** 1.6.x — toolkit oficial WinUI 3
- **Microsoft.Windows.SDK.BuildTools** — geração de Win32 metadata
- **CommunityToolkit.Mvvm** — `ObservableObject`, `RelayCommand`
- **Pty.Net** — wrapper ConPTY pra spawn de shell com TTY
- **Microsoft.Extensions.DependencyInjection** — DI básico

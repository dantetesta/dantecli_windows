using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DanteCLI.Models;
using DanteCLI.ViewModels;

namespace DanteCLI.Views;

public partial class AILauncherToolbar : UserControl
{
    public AILauncherToolbar()
    {
        InitializeComponent();
        Loaded += (_, _) => Render();
        AppState.Shared.AIProviders.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(new System.Action(Render));
    }

    private void Render()
    {
        Root.Children.Clear();

        var splitBtn = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4, 8, 4),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Visão Dividida",
        };
        var splitContent = new StackPanel { Orientation = Orientation.Horizontal };
        splitContent.Children.Add(new TextBlock { Text = "🪟", FontSize = 13, Margin = new Thickness(0,0,4,0) });
        splitContent.Children.Add(new TextBlock {
            Text = AppState.Shared.SplitWorkspace is null ? "Visão Dividida" : "Sair do split",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        splitBtn.Content = splitContent;
        splitBtn.Click += (_, _) =>
        {
            if (AppState.Shared.SplitWorkspace is not null)
            {
                AppState.Shared.ExitSplitWorkspace();
            }
            else
            {
                var win = Window.GetWindow(this);
                var picker = new SplitWorkspacePicker { Owner = win };
                picker.ShowDialog();
            }
            Render();
        };
        Root.Children.Add(splitBtn);

        AppState.Shared.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppState.SplitWorkspace))
                Dispatcher.BeginInvoke(new System.Action(Render));
        };

        foreach (var p in AppState.Shared.AIProviders.Where(p => p.Enabled))
            Root.Children.Add(MakeButton(p));
    }

    private static Button MakeButton(AIProvider provider)
    {
        var btn = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4, 8, 4),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = $"{provider.Name} — clique injeta na aba ativa, Alt+clique abre nova aba",
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (!string.IsNullOrEmpty(provider.Emoji))
            content.Children.Add(new TextBlock { Text = provider.Emoji, FontSize = 13, Margin = new Thickness(0,0,4,0), VerticalAlignment = VerticalAlignment.Center });
        else
            content.Children.Add(new Ellipse {
                Width = 8, Height = 8,
                Fill = new SolidColorBrush(TabChip.ColorFromHex(provider.ColorHex)),
                Margin = new Thickness(0,0,4,0),
                VerticalAlignment = VerticalAlignment.Center
            });
        content.Children.Add(new TextBlock { Text = provider.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        btn.Content = content;
        btn.Click += (_, _) =>
        {
            var inNewTab = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            AppState.Shared.LaunchAI(provider, inNewTab);
        };
        return btn;
    }
}

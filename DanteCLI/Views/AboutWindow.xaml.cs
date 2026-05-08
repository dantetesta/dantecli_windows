using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace DanteCLI.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var v = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "?";
        VersionText.Text = $"Versão {v}";
        CopyrightText.Text = $"© {DateTime.Now.Year} Dante Testa";
    }

    private void SiteBtn_Click(object sender, RoutedEventArgs e) =>
        Open("https://dantetesta.com.br");

    private void EmailBtn_Click(object sender, RoutedEventArgs e) =>
        Open("mailto:suporte@dantetesta.com.br");

    private void ZapBtn_Click(object sender, RoutedEventArgs e) =>
        Open("https://wa.me/5519998021956");

    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }
}

using System.Windows;

namespace DanteCLI.Views;

public partial class TextInputDialog : Window
{
    public string Value { get; private set; } = "";

    public TextInputDialog(string title, string prompt, string initial = "")
    {
        InitializeComponent();
        Title = title;
        Prompt.Text = prompt;
        Input.Text = initial;
        Loaded += (_, _) => { Input.Focus(); Input.SelectAll(); };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Value = Input.Text;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

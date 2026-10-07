using System.Windows;
using PW64Editor.Core.Text;

namespace PW64Editor.App.Views;

/// <summary>
/// Explains how texts are written in the editor and lists every single text code with its meaning.
/// </summary>
public partial class TextCodesWindow : Window
{
    public TextCodesWindow(TextFont font)
    {
        InitializeComponent();
        MarkupList.ItemsSource = TextMarkup.Elements;
        CodeList.ItemsSource = TextMarkup.DescribeCodes(font);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

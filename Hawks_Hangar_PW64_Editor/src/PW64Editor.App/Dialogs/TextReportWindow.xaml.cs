using System.Windows;

namespace PW64Editor.App.Dialogs;

/// <summary>
/// Shows a text report (ROM info, MIO0 scan, chunk list) that can be copied.
/// </summary>
public partial class TextReportWindow : Window
{
    private TextReportWindow()
    {
        InitializeComponent();
    }

    /// <summary>Shows a report as a modal window.</summary>
    public static void ShowReport(Window owner, string title, string text)
    {
        var window = new TextReportWindow { Owner = owner, Title = title };
        window.ReportBox.Text = text;
        window.ShowDialog();
    }

    private void OnCopy(object sender, RoutedEventArgs e) => Clipboard.SetText(ReportBox.Text);

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

using System.Windows;
using PW64Editor.Core.Text;

namespace PW64Editor.App.Dialogs;

/// <summary>
/// Lists the rows of an imported CSV file that were not imported, with a short reason each.
/// </summary>
public partial class TextImportReportDialog : Window
{
    public TextImportReportDialog(string summary, IReadOnlyList<TextImportRejection> rejected)
    {
        InitializeComponent();
        SummaryText.Text = summary;
        ProblemList.ItemsSource = rejected;
    }
}

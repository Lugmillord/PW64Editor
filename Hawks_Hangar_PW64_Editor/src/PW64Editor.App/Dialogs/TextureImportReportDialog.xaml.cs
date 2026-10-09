using System.Windows;
using PW64Editor.Core.Textures;

namespace PW64Editor.App.Dialogs;

/// <summary>
/// Lists the files of an imported ZIP file that were not imported, with a short reason each.
/// </summary>
public partial class TextureImportReportDialog : Window
{
    public TextureImportReportDialog(string summary, IReadOnlyList<TextureImportRejection> rejected)
    {
        InitializeComponent();
        SummaryText.Text = summary;
        ProblemList.ItemsSource = rejected;
    }
}

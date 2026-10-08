using System.Windows;
using PW64Editor.Core.Code;

namespace PW64Editor.App.Dialogs;

/// <summary>
/// Shown when a project is opened that lacks some of the editor's code fixes. "Fix it" returns
/// true; the caller then applies the fixes and rebuilds. "Not now" asks again next time.
/// </summary>
public partial class CodeFixOfferDialog : Window
{
    public CodeFixOfferDialog(IReadOnlyList<CodeFix> missing)
    {
        InitializeComponent();
        FixList.ItemsSource = missing.Select(f => new CodeFixRow(f, isApplied: false)).ToList();
    }

    /// <summary>Whether the user wants a restore point before the fixes are applied.</summary>
    public bool CreateRestorePoint => RestorePointBox.IsChecked == true;

    private void OnFixIt(object sender, RoutedEventArgs e) => DialogResult = true;
}

using System.ComponentModel;
using PW64Editor.Core.Code;

namespace PW64Editor.App.Dialogs;

/// <summary>
/// One code fix as shown in the dialogs. Public properties, because WPF data binding reads them.
/// </summary>
public sealed class CodeFixRow : INotifyPropertyChanged
{
    private bool _isApplied;

    public CodeFixRow(CodeFix fix, bool isApplied)
    {
        Fix = fix;
        _isApplied = isApplied;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CodeFix Fix { get; }

    public string Name => Fix.Name;

    public string Problem => Fix.Problem;

    public string Solution => Fix.Solution;

    /// <summary>True once the fix is part of the project.</summary>
    public bool IsApplied
    {
        get => _isApplied;
        set
        {
            _isApplied = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsApplied)));
        }
    }
}

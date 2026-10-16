using System.ComponentModel;
using PW64Editor.App.Services;
using PW64Editor.Core.Code;

namespace PW64Editor.App.Dialogs;

/// <summary>
/// One game patch as shown in the project settings. Public properties, because WPF data binding reads them.
/// </summary>
public sealed class GamePatchRow : INotifyPropertyChanged
{
    private bool _isApplied;

    public GamePatchRow(GamePatch patch, bool isApplied)
    {
        Patch = patch;
        _isApplied = isApplied;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public GamePatch Patch { get; }

    public string Name => L.T(Patch.Name);

    public string Description => L.T(Patch.Description);

    /// <summary>True while the patch is part of the project.</summary>
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

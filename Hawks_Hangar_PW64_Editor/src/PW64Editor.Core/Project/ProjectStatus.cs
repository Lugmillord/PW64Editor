using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Project;

/// <summary>State of one replacement file compared to the original.</summary>
public enum OverrideState
{
    /// <summary>Content is identical to the original (no effect on the build).</summary>
    Unchanged,

    /// <summary>Same size, different content.</summary>
    Modified,

    /// <summary>Different size: the file table will be rewritten.</summary>
    Resized,

    /// <summary>The file cannot be used; see the message.</summary>
    Invalid,
}

/// <summary>
/// Comparison of one replacement file with the original game file.
/// </summary>
public sealed record OverrideStatus(FileOverride Override, OverrideState State, int OriginalSize, int NewSize, string Message);

/// <summary>
/// Compares the project's replacement files with the original game files, without building.
/// </summary>
public static class ProjectStatus
{
    public static IReadOnlyList<OverrideStatus> Compute(HackProject project, GameFileSystem cleanFileSystem)
    {
        var result = new List<OverrideStatus>();

        foreach (FileOverride replacement in project.GetOverrides())
        {
            GameFile? original = cleanFileSystem.Files.FirstOrDefault(f => f.TableIndex == replacement.TableIndex);
            byte[] data = File.ReadAllBytes(replacement.Path);

            if (original is null)
            {
                result.Add(new(replacement, OverrideState.Invalid, 0, data.Length, CoreText.T("no game file with this index")));
            }
            else if (original.FileType != replacement.FileType)
            {
                result.Add(new(replacement, OverrideState.Invalid, original.Size, data.Length,
                    CoreText.F("type in name is '{0}', game file is '{1}'", replacement.FileType, original.FileType)));
            }
            else if (data.AsSpan().SequenceEqual(original.Data))
            {
                result.Add(new(replacement, OverrideState.Unchanged, original.Size, data.Length, CoreText.T("identical to original")));
            }
            else if (data.Length == original.Size)
            {
                result.Add(new(replacement, OverrideState.Modified, original.Size, data.Length, CountDifferences(original.Data, data)));
            }
            else
            {
                result.Add(new(replacement, OverrideState.Resized, original.Size, data.Length,
                    CoreText.F("{0:+#,0;-#,0} bytes", data.Length - original.Size)));
            }
        }

        return result;
    }

    private static string CountDifferences(byte[] a, byte[] b)
    {
        int count = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
            {
                count++;
            }
        }

        return count == 1 ? CoreText.T("1 byte changed") : CoreText.F("{0:N0} bytes changed", count);
    }
}

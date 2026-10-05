using System.Globalization;

namespace PW64Editor.Cli.Commands;

/// <summary>
/// Parses numbers given on the command line. ROM offsets are usually written in hex,
/// so both "0xDE754" and plain decimal "911188" are accepted.
/// </summary>
internal static class HexParser
{
    public static bool TryParseOffset(string text, out int value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}

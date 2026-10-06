namespace PW64Editor.Core.Patching;

/// <summary>
/// Constants and the variable-length number encoding of the BPS patch format.
/// </summary>
/// <remarks>
/// <para>
/// BPS ("beat patch system") was designed by byuu/Near and released into the public domain.
/// This implementation follows the official specification (bps_spec.md, distributed with Flips
/// and byuu's beat). Overview of the file layout:
/// </para>
/// <code>
/// "BPS1"
/// number   source size
/// number   target size
/// number   metadata size, followed by that many bytes of metadata (usually UTF-8 text)
/// actions  until 12 bytes before the end; each starts with number ((length - 1) &lt;&lt; 2) | action
///          0 SourceRead : copy bytes from the source at the current output position
///          1 TargetRead : the bytes follow directly in the patch
///          2 SourceCopy : number (offset), then copy from the source at a relative position
///          3 TargetCopy : number (offset), then copy from already written output
/// u32 LE   CRC-32 of the source
/// u32 LE   CRC-32 of the target
/// u32 LE   CRC-32 of the patch itself (all bytes before this field)
/// </code>
/// </remarks>
internal static class BpsFormat
{
    public static ReadOnlySpan<byte> Magic => "BPS1"u8;

    /// <summary>Three CRC-32 values at the end of every patch.</summary>
    public const int FooterSize = 12;

    public const int ActionSourceRead = 0;
    public const int ActionTargetRead = 1;
    public const int ActionSourceCopy = 2;
    public const int ActionTargetCopy = 3;

    /// <summary>
    /// Writes a number in BPS variable-length encoding: 7 bits per byte, low bits first,
    /// the high bit marks the last byte. After each byte, one is subtracted from the
    /// remaining value, so every number has exactly one encoding.
    /// </summary>
    public static void WriteNumber(List<byte> output, ulong value)
    {
        while (true)
        {
            byte low = (byte)(value & 0x7F);
            value >>= 7;
            if (value == 0)
            {
                output.Add((byte)(0x80 | low));
                return;
            }

            output.Add(low);
            value--;
        }
    }

    /// <summary>Returns how many bytes <see cref="WriteNumber"/> would write for a value.</summary>
    public static int GetNumberLength(ulong value)
    {
        int length = 1;
        while ((value >>= 7) != 0)
        {
            value--;
            length++;
        }

        return length;
    }

    /// <summary>
    /// Encodes a signed offset for SourceCopy/TargetCopy: absolute value shifted left by one,
    /// with the lowest bit set for negative values.
    /// </summary>
    public static ulong EncodeOffset(long offset) =>
        offset < 0 ? ((ulong)(-offset) << 1) | 1 : (ulong)offset << 1;
}

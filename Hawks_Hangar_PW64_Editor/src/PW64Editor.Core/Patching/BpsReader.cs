using System.Buffers.Binary;
using System.Text;
using PW64Editor.Core.Hashing;

namespace PW64Editor.Core.Patching;

/// <summary>
/// Header and footer information of a BPS patch.
/// </summary>
/// <param name="SourceSize">Size of the file the patch must be applied to.</param>
/// <param name="TargetSize">Size of the file the patch produces.</param>
/// <param name="SourceCrc32">CRC-32 of the required source file.</param>
/// <param name="TargetCrc32">CRC-32 of the resulting file.</param>
/// <param name="PatchCrc32">CRC-32 of the patch itself (already verified when this record exists).</param>
/// <param name="Metadata">Optional metadata text stored in the patch (empty if none).</param>
public sealed record BpsPatchInfo(
    long SourceSize,
    long TargetSize,
    uint SourceCrc32,
    uint TargetCrc32,
    uint PatchCrc32,
    string Metadata);

/// <summary>
/// Reads and applies BPS patches.
/// </summary>
public static class BpsReader
{
    /// <summary>
    /// Reads the header and footer of a patch and verifies the patch checksum.
    /// </summary>
    /// <exception cref="BpsException">The patch is invalid or damaged.</exception>
    public static BpsPatchInfo ReadInfo(ReadOnlySpan<byte> patch)
    {
        var cursor = new PatchCursor(patch);
        return ReadHeaderAndFooter(patch, ref cursor);
    }

    /// <summary>
    /// Applies a patch and returns the resulting file.
    /// </summary>
    /// <remarks>
    /// All three checksums are verified: the patch must be intact, the source must be the exact
    /// file the patch was made for, and the result must match the expected target.
    /// </remarks>
    /// <exception cref="BpsException">The patch is invalid, or the source is the wrong file.</exception>
    public static byte[] Apply(ReadOnlySpan<byte> source, ReadOnlySpan<byte> patch)
    {
        var cursor = new PatchCursor(patch);
        BpsPatchInfo info = ReadHeaderAndFooter(patch, ref cursor);

        if (source.Length != info.SourceSize)
        {
            throw new BpsException(BpsError.WrongSource,
                $"The input file has {source.Length:N0} bytes, but the patch expects {info.SourceSize:N0} bytes.");
        }

        uint sourceCrc = Crc32.Compute(source);
        if (sourceCrc != info.SourceCrc32)
        {
            throw new BpsException(BpsError.WrongSource,
                $"The input file has CRC-32 {sourceCrc:X8}, but the patch expects {info.SourceCrc32:X8}. " +
                "It is not the file this patch was made for.");
        }

        if (info.TargetSize > Array.MaxLength)
        {
            throw new BpsException(BpsError.InvalidFormat, $"Target size {info.TargetSize:N0} is too large.");
        }

        byte[] target = new byte[info.TargetSize];
        int outputOffset = 0;
        long sourceRelativeOffset = 0;
        long targetRelativeOffset = 0;
        int actionsEnd = patch.Length - BpsFormat.FooterSize;

        while (cursor.Position < actionsEnd)
        {
            ulong data = cursor.ReadNumber();
            int action = (int)(data & 3);
            ulong length = (data >> 2) + 1;

            if (length > (ulong)(target.Length - outputOffset))
            {
                throw Invalid($"Action at patch offset 0x{cursor.Position:X} writes beyond the end of the target.");
            }

            int count = (int)length;
            switch (action)
            {
                case BpsFormat.ActionSourceRead:
                    if (outputOffset + count > source.Length)
                    {
                        throw Invalid("SourceRead reads beyond the end of the source.");
                    }

                    source.Slice(outputOffset, count).CopyTo(target.AsSpan(outputOffset));
                    outputOffset += count;
                    break;

                case BpsFormat.ActionTargetRead:
                    cursor.ReadBytes(count, actionsEnd).CopyTo(target.AsSpan(outputOffset));
                    outputOffset += count;
                    break;

                case BpsFormat.ActionSourceCopy:
                    sourceRelativeOffset += cursor.ReadOffset();
                    if (sourceRelativeOffset < 0 || sourceRelativeOffset + count > source.Length)
                    {
                        throw Invalid("SourceCopy reads outside the source.");
                    }

                    source.Slice((int)sourceRelativeOffset, count).CopyTo(target.AsSpan(outputOffset));
                    sourceRelativeOffset += count;
                    outputOffset += count;
                    break;

                case BpsFormat.ActionTargetCopy:
                    targetRelativeOffset += cursor.ReadOffset();
                    if (targetRelativeOffset < 0 || targetRelativeOffset >= outputOffset)
                    {
                        throw Invalid("TargetCopy reads data that has not been written yet.");
                    }

                    // Byte by byte on purpose: source and destination may overlap,
                    // which is how BPS encodes runs of repeated bytes.
                    for (int i = 0; i < count; i++)
                    {
                        target[outputOffset++] = target[targetRelativeOffset++];
                    }

                    break;
            }
        }

        if (outputOffset != target.Length)
        {
            throw Invalid($"The patch produced {outputOffset:N0} bytes instead of {target.Length:N0}.");
        }

        uint targetCrc = Crc32.Compute(target);
        if (targetCrc != info.TargetCrc32)
        {
            throw new BpsException(BpsError.TargetMismatch,
                $"The result has CRC-32 {targetCrc:X8}, but the patch expects {info.TargetCrc32:X8}.");
        }

        return target;
    }

    private static BpsPatchInfo ReadHeaderAndFooter(ReadOnlySpan<byte> patch, ref PatchCursor cursor)
    {
        if (patch.Length < BpsFormat.Magic.Length + 3 + BpsFormat.FooterSize
            || !patch.StartsWith(BpsFormat.Magic))
        {
            throw Invalid("The file is not a BPS patch (missing \"BPS1\" header).");
        }

        // Verify the patch checksum first: if the file is damaged, everything else is meaningless.
        ReadOnlySpan<byte> footer = patch[^BpsFormat.FooterSize..];
        uint sourceCrc = BinaryPrimitives.ReadUInt32LittleEndian(footer);
        uint targetCrc = BinaryPrimitives.ReadUInt32LittleEndian(footer[4..]);
        uint patchCrc = BinaryPrimitives.ReadUInt32LittleEndian(footer[8..]);

        uint actualPatchCrc = Crc32.Compute(patch[..^4]);
        if (actualPatchCrc != patchCrc)
        {
            throw new BpsException(BpsError.PatchCorrupted,
                $"The patch file is damaged (checksum {actualPatchCrc:X8}, expected {patchCrc:X8}).");
        }

        cursor.Position = BpsFormat.Magic.Length;
        ulong sourceSize = cursor.ReadNumber();
        ulong targetSize = cursor.ReadNumber();
        ulong metadataSize = cursor.ReadNumber();

        if (sourceSize > long.MaxValue || targetSize > long.MaxValue)
        {
            throw Invalid("File sizes in the patch header are out of range.");
        }

        if (metadataSize > (ulong)(patch.Length - BpsFormat.FooterSize - cursor.Position))
        {
            throw Invalid("Metadata extends beyond the end of the patch.");
        }

        string metadata = Encoding.UTF8.GetString(cursor.ReadBytes((int)metadataSize, patch.Length - BpsFormat.FooterSize));

        return new BpsPatchInfo((long)sourceSize, (long)targetSize, sourceCrc, targetCrc, patchCrc, metadata);
    }

    private static BpsException Invalid(string message) => new(BpsError.InvalidFormat, message);

    /// <summary>A read position inside the patch with helpers for the BPS encodings.</summary>
    private ref struct PatchCursor(ReadOnlySpan<byte> patch)
    {
        private readonly ReadOnlySpan<byte> _patch = patch;

        public int Position { get; set; }

        public ulong ReadNumber()
        {
            ulong data = 0;
            ulong shift = 1;
            int limit = _patch.Length - BpsFormat.FooterSize;

            for (int bytes = 0; ; bytes++)
            {
                if (Position >= limit || bytes >= 10)
                {
                    throw Invalid("Malformed number in patch.");
                }

                byte x = _patch[Position++];
                data += (ulong)(x & 0x7F) * shift;
                if ((x & 0x80) != 0)
                {
                    return data;
                }

                shift <<= 7;
                data += shift;
            }
        }

        public long ReadOffset()
        {
            ulong data = ReadNumber();
            long magnitude = (long)(data >> 1);
            return (data & 1) != 0 ? -magnitude : magnitude;
        }

        public ReadOnlySpan<byte> ReadBytes(int count, int limit)
        {
            if (count > limit - Position)
            {
                throw Invalid("Patch data ends unexpectedly.");
            }

            ReadOnlySpan<byte> bytes = _patch.Slice(Position, count);
            Position += count;
            return bytes;
        }
    }
}

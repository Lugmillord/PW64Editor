using PW64Editor.Core.Patching;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Verification;

namespace PW64Editor.Cli.Commands;

/// <summary>
/// BPS patch commands: "bps-create", "bps-apply" and "bps-info".
/// </summary>
/// <remarks>
/// ROMs are always loaded through <see cref="N64Rom"/>, which normalizes .v64/.n64 files to
/// big-endian. Patches are therefore always created against, and applied to, the .z64 layout,
/// no matter which format the user has on disk.
/// </remarks>
internal static class PatchCommands
{
    public static int Create(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: pw64cli bps-create <clean rom> <modified rom> <patch.bps>");
            return ExitCodes.InvalidArguments;
        }

        if (!CommandHelpers.TryLoadRom(args[0], out N64Rom? clean)
            || !CommandHelpers.TryLoadRom(args[1], out N64Rom? modified))
        {
            return ExitCodes.InvalidRom;
        }

        RomVerificationResult verification = RomVerifier.Verify(clean);
        if (!verification.IsUsableAsBase)
        {
            // A patch made against a modified ROM only works for people with that exact file.
            Console.WriteLine($"Warning: the source is not a clean ROM. {verification.Message}");
        }

        Console.WriteLine("Creating patch...");
        byte[] patch = BpsWriter.Create(clean.Data, modified.Data);

        // Self-check: apply the patch we just made. If this ever fails, it is a bug in the
        // encoder, and we must not hand out a broken patch.
        byte[] check = BpsReader.Apply(clean.Data, patch);
        if (!check.AsSpan().SequenceEqual(modified.Data))
        {
            Console.Error.WriteLine("Internal error: the created patch does not reproduce the modified ROM.");
            return ExitCodes.UnexpectedError;
        }

        File.WriteAllBytes(args[2], patch);
        Console.WriteLine($"Patch saved to {args[2]} ({patch.Length:N0} bytes), verified.");
        return ExitCodes.Success;
    }

    public static int Apply(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: pw64cli bps-apply <clean rom> <patch.bps> <output>");
            return ExitCodes.InvalidArguments;
        }

        if (!CommandHelpers.TryLoadRom(args[0], out N64Rom? rom))
        {
            return ExitCodes.InvalidRom;
        }

        byte[] result;
        try
        {
            byte[] patch = File.ReadAllBytes(args[1]);
            result = BpsReader.Apply(rom.Data, patch);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Cannot read patch: {ex.Message}");
            return ExitCodes.InvalidArguments;
        }
        catch (BpsException ex)
        {
            Console.Error.WriteLine($"Cannot apply patch: {ex.Message}");
            if (ex.Error == BpsError.WrongSource)
            {
                Console.Error.WriteLine($"Use an unmodified {KnownRoms.PilotwingsUsa.Name} ROM. " +
                                        "The byte order (.z64/.v64/.n64) does not matter, it is converted automatically.");
            }

            return ExitCodes.VerificationFailed;
        }

        File.WriteAllBytes(args[2], result);
        Console.WriteLine($"Patched ROM saved to {args[2]} ({result.Length:N0} bytes).");
        return ExitCodes.Success;
    }

    public static int Info(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: pw64cli bps-info <patch.bps>");
            return ExitCodes.InvalidArguments;
        }

        BpsPatchInfo info;
        try
        {
            info = BpsReader.ReadInfo(File.ReadAllBytes(args[0]));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BpsException)
        {
            Console.Error.WriteLine($"Cannot read patch: {ex.Message}");
            return ExitCodes.InvalidArguments;
        }

        Console.WriteLine($"Source : {info.SourceSize,12:N0} bytes, CRC-32 {info.SourceCrc32:X8}");
        Console.WriteLine($"Target : {info.TargetSize,12:N0} bytes, CRC-32 {info.TargetCrc32:X8}");
        Console.WriteLine($"Patch  : CRC-32 {info.PatchCrc32:X8} (verified)");
        if (info.Metadata.Length > 0)
        {
            Console.WriteLine($"Metadata: {info.Metadata}");
        }

        return ExitCodes.Success;
    }
}

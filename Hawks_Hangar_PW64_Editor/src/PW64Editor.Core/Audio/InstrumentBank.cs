using System.Buffers.Binary;
using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Audio;

/// <summary>An envelope: rise to the attack volume, fall to the decay volume, hold, fade out on release.</summary>
/// <param name="AttackMicroseconds">Time to reach <paramref name="AttackVolume"/>.</param>
/// <param name="DecayMicroseconds">Time to reach <paramref name="DecayVolume"/> after that.</param>
/// <param name="ReleaseMicroseconds">Time to fade out after the note ends.</param>
/// <param name="AttackVolume">0-127.</param>
/// <param name="DecayVolume">0-127, held while the note lasts.</param>
public sealed record SoundEnvelope(int AttackMicroseconds, int DecayMicroseconds, int ReleaseMicroseconds, int AttackVolume, int DecayVolume);

/// <summary>One recorded sound of an instrument and the keys it is used for.</summary>
/// <param name="KeyMin">Lowest key (MIDI number).</param>
/// <param name="KeyMax">Highest key.</param>
/// <param name="KeyBase">The key at which the recording plays at its own pitch.</param>
/// <param name="Detune">Fine tuning in cents.</param>
/// <param name="VelocityMin">Lowest velocity it is used for.</param>
/// <param name="VelocityMax">Highest velocity.</param>
/// <param name="Volume">0-127.</param>
/// <param name="Envelope">Volume over time.</param>
/// <param name="Samples">The decoded recording (16-bit).</param>
/// <param name="LoopStart">Start of the part that repeats while the note is held, or -1.</param>
/// <param name="LoopEnd">End of the repeating part.</param>
public sealed record InstrumentSound(int KeyMin, int KeyMax, int KeyBase, int Detune, int VelocityMin, int VelocityMax, int Volume,
    SoundEnvelope Envelope, short[] Samples, int LoopStart, int LoopEnd);

/// <summary>An instrument of the bank.</summary>
/// <param name="Program">Its number (the program number in the songs).</param>
/// <param name="Volume">0-127.</param>
/// <param name="Sounds">Its sounds.</param>
public sealed record BankInstrument(int Program, int Volume, IReadOnlyList<InstrumentSound> Sounds);

/// <summary>
/// The game's instrument bank: the instrument descriptions (".ctl", ALBankFile revision "B1") and
/// the recorded sounds (".tbl", VADPCM compressed), see bnkf.c and the N64 audio documentation.
/// </summary>
public sealed class InstrumentBank
{
    private InstrumentBank(int sampleRate, int percussion, IReadOnlyDictionary<int, BankInstrument> instruments)
    {
        SampleRate = sampleRate;
        PercussionProgram = percussion;
        Instruments = instruments;
    }

    /// <summary>Playback rate of the recordings (22050 Hz in Pilotwings 64).</summary>
    public int SampleRate { get; }

    /// <summary>The instrument that channel 10 starts with, or -1.</summary>
    public int PercussionProgram { get; }

    /// <summary>The instruments by program number (only the used places of the bank).</summary>
    public IReadOnlyDictionary<int, BankInstrument> Instruments { get; }

    /// <summary>The first instrument of the bank (every channel starts with it, except channel 10).</summary>
    public int FirstProgram => Instruments.Keys.DefaultIfEmpty(0).Min();

    /// <summary>Reads the bank.</summary>
    /// <param name="ctl">The instrument descriptions.</param>
    /// <param name="tbl">The recordings.</param>
    /// <exception cref="InvalidDataException">Damaged data.</exception>
    public static InstrumentBank Parse(byte[] ctl, byte[] tbl)
    {
        try
        {
            if (ctl.Length < 8 || ctl[0] != (byte)'B' || ctl[1] != (byte)'1')
            {
                throw new InvalidDataException(CoreText.T("This is not the game's instrument bank."));
            }

            int bank = (int)U32(ctl, 4);
            int count = S16(ctl, bank);
            int sampleRate = (int)U32(ctl, bank + 4);
            int percussionOffset = (int)U32(ctl, bank + 8);
            var instruments = new Dictionary<int, BankInstrument>();
            var decoded = new Dictionary<int, short[]>();
            int percussion = -1;

            for (int program = 0; program < count; program++)
            {
                int offset = (int)U32(ctl, bank + 12 + program * 4);
                if (offset == 0)
                {
                    continue;
                }

                if (offset == percussionOffset)
                {
                    percussion = program;
                }

                int soundCount = S16(ctl, offset + 14);
                var sounds = new List<InstrumentSound>();
                for (int i = 0; i < soundCount; i++)
                {
                    sounds.Add(ReadSound(ctl, tbl, (int)U32(ctl, offset + 16 + i * 4), decoded));
                }

                instruments[program] = new BankInstrument(program, ctl[offset], sounds);
            }

            return new InstrumentBank(sampleRate, percussion, instruments);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            throw new InvalidDataException(CoreText.T("This is not the game's instrument bank."), ex);
        }
    }

    private static InstrumentSound ReadSound(byte[] ctl, byte[] tbl, int offset, Dictionary<int, short[]> decoded)
    {
        int envelope = (int)U32(ctl, offset), keyMap = (int)U32(ctl, offset + 4), wave = (int)U32(ctl, offset + 8);
        var env = new SoundEnvelope((int)U32(ctl, envelope), (int)U32(ctl, envelope + 4), (int)U32(ctl, envelope + 8),
            ctl[envelope + 12], ctl[envelope + 13]);

        int baseOffset = (int)U32(ctl, wave), length = (int)U32(ctl, wave + 4), type = ctl[wave + 8];
        int loop = (int)U32(ctl, wave + 12);
        if (!decoded.TryGetValue(wave, out short[]? samples))
        {
            samples = type == 0
                ? Vadpcm.Decode(tbl.AsSpan(baseOffset, length), ReadBook(ctl, (int)U32(ctl, wave + 16)))
                : Enumerable.Range(0, length / 2).Select(i => (short)BinaryPrimitives.ReadInt16BigEndian(tbl.AsSpan(baseOffset + i * 2))).ToArray();
            decoded[wave] = samples;
        }

        int loopStart = -1, loopEnd = 0;
        if (loop != 0 && (int)U32(ctl, loop + 8) != 0)
        {
            loopStart = (int)U32(ctl, loop);
            loopEnd = (int)U32(ctl, loop + 4);
            if (loopStart >= loopEnd || loopEnd > samples.Length)
            {
                loopStart = -1;
            }
        }

        return new InstrumentSound(ctl[keyMap + 2], ctl[keyMap + 3], ctl[keyMap + 4], (sbyte)ctl[keyMap + 5], ctl[keyMap], ctl[keyMap + 1],
            ctl[offset + 13], env, samples, loopStart, loopEnd);
    }

    private static Vadpcm.Book ReadBook(byte[] ctl, int offset)
    {
        int order = (int)U32(ctl, offset), predictors = (int)U32(ctl, offset + 4);
        short[] coefficients = new short[order * predictors * 8];
        for (int i = 0; i < coefficients.Length; i++)
        {
            coefficients[i] = (short)S16(ctl, offset + 8 + i * 2);
        }

        return new Vadpcm.Book(order, predictors, coefficients);
    }

    private static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));

    private static int S16(byte[] data, int offset) => BinaryPrimitives.ReadInt16BigEndian(data.AsSpan(offset, 2));
}

/// <summary>Decoder for the N64's VADPCM sound compression (9 bytes → 16 samples).</summary>
public static class Vadpcm
{
    /// <summary>The prediction coefficients: <c>Predictors</c> × <c>Order</c> × 8 values.</summary>
    public sealed record Book(int Order, int Predictors, short[] Coefficients);

    /// <summary>Decodes a recording.</summary>
    public static short[] Decode(ReadOnlySpan<byte> data, Book book)
    {
        int order = book.Order;
        int[][][] tables = BuildTables(book);
        int frames = data.Length / 9;
        short[] output = new short[frames * 16];
        int[] previous = new int[16];
        int[] current = new int[16];
        int[] input = new int[16];
        int[] vector = new int[order + 8];

        for (int frame = 0; frame < frames; frame++)
        {
            ReadOnlySpan<byte> bytes = data.Slice(frame * 9, 9);
            int scale = 1 << (bytes[0] >> 4);
            int[][] table = tables[Math.Min(bytes[0] & 0x0F, book.Predictors - 1)];
            for (int i = 0; i < 8; i++)
            {
                int high = bytes[1 + i] >> 4, low = bytes[1 + i] & 0x0F;
                input[i * 2] = (high > 7 ? high - 16 : high) * scale;
                input[i * 2 + 1] = (low > 7 ? low - 16 : low) * scale;
            }

            for (int half = 0; half < 2; half++)
            {
                for (int i = 0; i < 8; i++)
                {
                    vector[order + i] = input[half * 8 + i];
                }

                for (int i = 0; i < order; i++)
                {
                    vector[i] = half == 0 ? previous[16 - order + i] : current[8 - order + i];
                }

                for (int i = 0; i < 8; i++)
                {
                    long total = 0;
                    for (int k = 0; k < order + 8; k++)
                    {
                        total += (long)table[i][k] * vector[k];
                    }

                    current[half * 8 + i] = (int)Math.Clamp(total >> 11, short.MinValue, short.MaxValue);
                }
            }

            for (int i = 0; i < 16; i++)
            {
                output[frame * 16 + i] = (short)current[i];
                previous[i] = current[i];
            }
        }

        return output;
    }

    /// <summary>Expands the book like the SDK's decoder (vadpcm_dec): one row of order + 8 factors per output sample.</summary>
    private static int[][][] BuildTables(Book book)
    {
        int order = book.Order;
        var tables = new int[book.Predictors][][];
        for (int p = 0; p < book.Predictors; p++)
        {
            int[][] t = Enumerable.Range(0, 8).Select(_ => new int[order + 8]).ToArray();
            for (int k = 0; k < order; k++)
            {
                for (int j = 0; j < 8; j++)
                {
                    t[j][k] = book.Coefficients[(p * order + k) * 8 + j];
                }
            }

            for (int k = 1; k < 8; k++)
            {
                t[k][order] = t[k - 1][order - 1];
            }

            t[0][order] = 1 << 11;
            for (int k = 1; k < 8; k++)
            {
                for (int j = 0; j < k; j++)
                {
                    t[j][k + order] = 0;
                }

                for (int j = k; j < 8; j++)
                {
                    t[j][k + order] = t[j - k][order];
                }
            }

            tables[p] = t;
        }

        return tables;
    }
}

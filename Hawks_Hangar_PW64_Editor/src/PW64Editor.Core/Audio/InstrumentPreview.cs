using System.Buffers.Binary;

namespace PW64Editor.Core.Audio;

/// <summary>
/// Short sound samples of instruments, rendered like the game plays them (pitch, volume envelope,
/// repeating part), without reverb.
/// </summary>
public static class InstrumentPreview
{
    /// <summary>
    /// A sample of an instrument as a WAV file: a scale over one octave and a held note, for drum
    /// sets each of their sounds once.
    /// </summary>
    /// <param name="bank">The instrument bank.</param>
    /// <param name="program">The instrument.</param>
    /// <param name="centerKey">A key in the range the song uses, or null for the instrument's own range.</param>
    public static byte[] RenderWav(InstrumentBank bank, int program, int? centerKey = null)
    {
        BankInstrument instrument = bank.Instruments[program];
        int rate = bank.SampleRate;
        var output = new List<float>();
        if (IsDrumSet(instrument))
        {
            foreach (int key in instrument.Sounds.Select(s => s.KeyMin).Distinct().Order())
            {
                output.AddRange(RenderNote(instrument, key, 0.45, rate));
            }
        }
        else
        {
            int center = centerKey ?? instrument.Sounds[0].KeyBase;
            int c = (center - 6) / 12 * 12;
            foreach (int step in new[] { 0, 2, 4, 5, 7, 9, 11, 12 })
            {
                output.AddRange(RenderNote(instrument, c + step, 0.3, rate));
            }

            output.AddRange(new float[(int)(0.15 * rate)]);
            output.AddRange(RenderNote(instrument, c, 1.5, rate));
        }

        return ToWav(output, rate);
    }

    /// <summary>True for instruments whose keys are all different single sounds (drum sets).</summary>
    public static bool IsDrumSet(BankInstrument instrument) =>
        instrument.Sounds.Count > 4 && instrument.Sounds.All(s => s.KeyMin == s.KeyMax);

    private static float[] RenderNote(BankInstrument instrument, int key, double seconds, int rate, int velocity = 100)
    {
        InstrumentSound? sound = instrument.Sounds.FirstOrDefault(s => s.KeyMin <= key && key <= s.KeyMax
                                                                     && s.VelocityMin <= velocity && velocity <= s.VelocityMax);
        if (sound is null || sound.Samples.Length < 2)
        {
            return new float[(int)(seconds * rate)];
        }

        double ratio = Math.Pow(2, (key - sound.KeyBase) / 12.0 + sound.Detune / 1200.0);
        SoundEnvelope env = sound.Envelope;
        double attack = Math.Max(env.AttackMicroseconds, 1) / 1e6, decay = Math.Max(env.DecayMicroseconds, 1) / 1e6;
        double release = Math.Min(Math.Max(env.ReleaseMicroseconds, 0) / 1e6, 1.0);
        double attackVolume = env.AttackVolume / 127.0, decayVolume = env.DecayVolume / 127.0;
        double gain = sound.Volume / 127.0 * instrument.Volume / 127.0 * velocity / 127.0;
        bool loops = sound.LoopStart >= 0;

        int count = (int)((seconds + release) * rate);
        float[] result = new float[count];
        double position = 0;
        for (int i = 0; i < count; i++)
        {
            double t = (double)i / rate;
            double amplitude = t < attack ? attackVolume * t / attack
                : t < attack + decay ? attackVolume + (decayVolume - attackVolume) * (t - attack) / decay
                : decayVolume;
            if (t > seconds)
            {
                amplitude *= release > 0 ? Math.Max(0, 1 - (t - seconds) / release) : 0;
            }

            int index = (int)position;
            if (loops && index >= sound.LoopEnd - 1)
            {
                position -= sound.LoopEnd - sound.LoopStart;
                index = (int)position;
            }

            if (index + 1 >= sound.Samples.Length)
            {
                break;
            }

            double fraction = position - index;
            result[i] = (float)((sound.Samples[index] * (1 - fraction) + sound.Samples[index + 1] * fraction) * amplitude * gain);
            position += ratio;
        }

        return result;
    }

    /// <summary>A 16-bit mono WAV file, normalized so the loudest point is at about 90 %.</summary>
    private static byte[] ToWav(List<float> samples, int rate)
    {
        float peak = samples.Count == 0 ? 1 : Math.Max(1, samples.Max(Math.Abs));
        float gain = Math.Min(1f, 30000f / peak);
        byte[] wav = new byte[44 + samples.Count * 2];
        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), wav.Length - 8);
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), 1); // PCM
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), 1); // mono
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), rate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), rate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), 16);
        "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), samples.Count * 2);
        for (int i = 0; i < samples.Count; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(44 + i * 2), (short)Math.Clamp(samples[i] * gain, short.MinValue, short.MaxValue));
        }

        return wav;
    }
}

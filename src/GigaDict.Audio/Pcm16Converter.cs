using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GigaDict.Audio;

public static class Pcm16Converter
{
    private const int TargetRate = 16000;

    public static byte[] Convert(byte[] sourceBytes, WaveFormat sourceFormat)
    {
        ArgumentNullException.ThrowIfNull(sourceBytes);
        using var source = new MemoryStream(sourceBytes, writable: false);
        return Convert(source, sourceFormat);
    }

    // Reads the source in place; the only full-size allocation is the PCM16 result.
    // Intermediate buffers that held samples are cleared before returning.
    public static byte[] Convert(Stream source, WaveFormat sourceFormat)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceFormat);
        using var raw = new RawSourceWaveStream(source, sourceFormat);
        ISampleProvider samples = raw.ToSampleProvider();
        if (sourceFormat.SampleRate != TargetRate) samples = new WdlResamplingSampleProvider(samples, TargetRate);

        var channels = samples.WaveFormat.Channels;
        var input = new float[4096 * channels];
        // Resampler latency may add a few frames; one extra second avoids regrowth.
        var frames = source.Length / Math.Max(1, sourceFormat.BlockAlign);
        var output = new byte[checked((int)((frames * TargetRate / sourceFormat.SampleRate + TargetRate) * 2))];
        var length = 0;
        try
        {
            int read;
            while ((read = samples.Read(input, 0, input.Length)) > 0)
            {
                var needed = length + read / channels * 2;
                if (needed > output.Length) output = Grow(output, length, needed);
                for (var frame = 0; frame + channels <= read; frame += channels)
                {
                    var mono = 0f;
                    for (var channel = 0; channel < channels; channel++) mono += input[frame + channel];
                    mono /= channels;
                    var value = (short)Math.Clamp(Math.Round(mono * 32767f), short.MinValue, short.MaxValue);
                    System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(length), value);
                    length += 2;
                }
            }
            return output.AsSpan(0, length).ToArray();
        }
        finally
        {
            Array.Clear(input);
            Array.Clear(output);
        }
    }

    private static byte[] Grow(byte[] current, int length, int needed)
    {
        var grown = new byte[Math.Max(needed, current.Length * 2)];
        current.AsSpan(0, length).CopyTo(grown);
        Array.Clear(current);
        return grown;
    }
}

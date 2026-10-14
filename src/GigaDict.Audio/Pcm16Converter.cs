using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GigaDict.Audio;

public static class Pcm16Converter
{
    public static byte[] Convert(ReadOnlySpan<byte> sourceBytes, WaveFormat sourceFormat)
    {
        using var source = new MemoryStream(sourceBytes.ToArray(), writable: false);
        using var raw = new RawSourceWaveStream(source, sourceFormat);
        ISampleProvider samples = raw.ToSampleProvider();
        if (sourceFormat.SampleRate != 16000) samples = new WdlResamplingSampleProvider(samples, 16000);

        var channels = samples.WaveFormat.Channels;
        var input = new float[4096 * channels];
        using var output = new MemoryStream();
        Span<byte> encoded = stackalloc byte[2];
        int read;
        while ((read = samples.Read(input, 0, input.Length)) > 0)
        {
            for (var frame = 0; frame + channels <= read; frame += channels)
            {
                var mono = 0f;
                for (var channel = 0; channel < channels; channel++) mono += input[frame + channel];
                mono /= channels;
                var value = (short)Math.Clamp(Math.Round(mono * 32767f), short.MinValue, short.MaxValue);
                System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(encoded, value);
                output.Write(encoded);
            }
        }
        return output.ToArray();
    }
}


using GigaDict.Audio;
using NAudio.Wave;

namespace GigaDict.Tests;

public sealed class PcmConverterTests
{
    [Fact]
    public void ConvertsStereo48KhzToMono16Khz()
    {
        const int frames = 48000;
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++) { samples[i * 2] = .5f; samples[i * 2 + 1] = -.5f; }
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        var result = Pcm16Converter.Convert(bytes, WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        Assert.InRange(result.Length, 31900, 32100);
        Assert.All(result, value => Assert.InRange(value, (byte)0, (byte)1));
    }
}

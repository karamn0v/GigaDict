using GigaDict.Audio;
using NAudio.Wave;

namespace GigaDict.Tests;

public sealed class AudioMemoryTests
{
    [Fact]
    public void ChunkedBufferRoundTripsAcrossChunkBoundaries()
    {
        var buffer = new ChunkedAudioBuffer();
        var data = new byte[ChunkedAudioBuffer.ChunkSize * 2 + 12345];
        Random.Shared.NextBytes(data);
        // Odd write sizes so writes straddle chunk boundaries.
        for (var offset = 0; offset < data.Length; offset += 7001)
            buffer.Write(data.AsSpan(offset, Math.Min(7001, data.Length - offset)));
        Assert.Equal(data.Length, buffer.Length);
        Assert.Equal(3, buffer.Chunks.Count);

        using var stream = buffer.OpenRead();
        var read = new byte[data.Length];
        var total = 0;
        int count;
        while ((count = stream.Read(read, total, Math.Min(5003, read.Length - total))) > 0) total += count;
        Assert.Equal(data, read);
    }

    [Fact]
    public void ClearZeroesEveryChunk()
    {
        var buffer = new ChunkedAudioBuffer();
        var data = new byte[ChunkedAudioBuffer.ChunkSize + 10];
        Array.Fill(data, (byte)0x5A);
        buffer.Write(data);
        var chunks = buffer.Chunks.ToArray();
        buffer.Clear();
        Assert.Equal(0, buffer.Length);
        Assert.All(chunks, chunk => Assert.All(chunk, value => Assert.Equal(0, value)));
    }

    [Fact]
    public void ConvertingFromChunksMatchesContiguousSource()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        var samples = new float[48000 * 2 * 3];
        for (var i = 0; i < samples.Length; i++) samples[i] = MathF.Sin(i * 0.01f) * 0.5f;
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        var buffer = new ChunkedAudioBuffer();
        buffer.Write(bytes);
        using var stream = buffer.OpenRead();
        Assert.Equal(Pcm16Converter.Convert(bytes, format), Pcm16Converter.Convert(stream, format));
    }

    [Fact]
    public async Task RecorderReturnsAudioLongerThanOneChunk()
    {
        var capture = new SimpleCapture();
        using var recorder = new WasapiRecorder(_ => capture);
        recorder.Start(null, TimeSpan.FromSeconds(120), 32000L * 120);
        var block = new byte[100_000];
        for (var i = 0; i < block.Length; i += 2) block[i] = 1;
        for (var i = 0; i < 25; i++) capture.Emit(block);
        var expected = Pcm16Converter.Convert(Enumerable.Repeat(block, 25).SelectMany(x => x).ToArray(), capture.WaveFormat);
        Assert.Equal(expected, await recorder.StopAsync());
    }

    private sealed class SimpleCapture : IWaveIn
    {
        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? RecordingStopped;
        public WaveFormat WaveFormat { get; set; } = new(16000, 16, 1);
        public void StartRecording() { }
        public void StopRecording() => RecordingStopped?.Invoke(this, new StoppedEventArgs());
        public void Emit(byte[] bytes) => DataAvailable?.Invoke(this, new WaveInEventArgs(bytes, bytes.Length));
        public void Dispose() { }
    }
}

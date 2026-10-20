namespace GigaDict.Audio;

// Capture buffer made of fixed chunks. Unlike MemoryStream it never reallocates,
// so growth leaves no uncleared copies of the audio on the heap, and conversion
// reads the chunks in place instead of requiring one contiguous copy.
internal sealed class ChunkedAudioBuffer
{
    internal const int ChunkSize = 1024 * 1024;
    private readonly List<byte[]> _chunks = [];

    public long Length { get; private set; }
    internal IReadOnlyList<byte[]> Chunks => _chunks;

    public void Write(ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty)
        {
            var offset = (int)(Length % ChunkSize);
            if (offset == 0 && Length / ChunkSize == _chunks.Count) _chunks.Add(new byte[ChunkSize]);
            var count = Math.Min(data.Length, ChunkSize - offset);
            data[..count].CopyTo(_chunks[^1].AsSpan(offset));
            data = data[count..];
            Length += count;
        }
    }

    public Stream OpenRead() => new ReadStream(this);

    public void Clear()
    {
        foreach (var chunk in _chunks) Array.Clear(chunk);
        _chunks.Clear();
        Length = 0;
    }

    private sealed class ReadStream(ChunkedAudioBuffer buffer) : Stream
    {
        private long _position;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => buffer.Length;

        public override long Position
        {
            get => _position;
            set => _position = Math.Clamp(value, 0, buffer.Length);
        }

        public override int Read(byte[] destination, int offset, int count) => Read(destination.AsSpan(offset, count));

        public override int Read(Span<byte> destination)
        {
            var total = 0;
            while (!destination.IsEmpty && _position < buffer.Length)
            {
                var chunk = buffer._chunks[(int)(_position / ChunkSize)];
                var chunkOffset = (int)(_position % ChunkSize);
                var count = (int)Math.Min(Math.Min(destination.Length, ChunkSize - chunkOffset), buffer.Length - _position);
                chunk.AsSpan(chunkOffset, count).CopyTo(destination);
                destination = destination[count..];
                _position += count;
                total += count;
            }
            return total;
        }

        public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => buffer.Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

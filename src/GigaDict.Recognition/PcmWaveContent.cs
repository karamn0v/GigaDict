using System.Net;
using System.Net.Http.Headers;

namespace GigaDict.Recognition;

internal sealed class PcmWaveContent(ReadOnlyMemory<byte> pcm) : HttpContent
{
    private const int HeaderLength = 44;

    public PcmWaveContent(ReadOnlyMemory<byte> pcm, bool setContentType = true) : this(pcm)
    {
        if (setContentType) Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var header = CreateHeader(pcm.Length);
        await stream.WriteAsync(header).ConfigureAwait(false);
        await stream.WriteAsync(pcm).ConfigureAwait(false);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = HeaderLength + pcm.Length;
        return true;
    }

    internal static byte[] CreateHeader(int pcmLength)
    {
        var header = new byte[HeaderLength];
        WriteAscii(header, 0, "RIFF");
        WriteInt32(header, 4, 36 + pcmLength);
        WriteAscii(header, 8, "WAVEfmt ");
        WriteInt32(header, 16, 16);
        WriteInt16(header, 20, 1);
        WriteInt16(header, 22, 1);
        WriteInt32(header, 24, 16000);
        WriteInt32(header, 28, 32000);
        WriteInt16(header, 32, 2);
        WriteInt16(header, 34, 16);
        WriteAscii(header, 36, "data");
        WriteInt32(header, 40, pcmLength);
        return header;
    }

    private static void WriteAscii(byte[] target, int offset, string value) =>
        System.Text.Encoding.ASCII.GetBytes(value, target.AsSpan(offset));
    private static void WriteInt16(byte[] target, int offset, short value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(target.AsSpan(offset), value);
    private static void WriteInt32(byte[] target, int offset, int value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(target.AsSpan(offset), value);
}


using System.Net;
using System.Text;
using GigaDict.Core.Tasks;
using GigaDict.Recognition;

namespace GigaDict.Tests;

public sealed class HttpTransportTests
{
    [Fact]
    public void OwnedClientDoesNotOverrideConfiguredRecognitionTimeoutWithOneHundredSeconds()
    {
        using var client = HttpTranscriptionTransport.CreateClient(TimeSpan.FromSeconds(10));
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [Fact]
    public async Task SendsWaveAsOctetStreamAndParsesText()
    {
        byte[]? body = null;
        var handler = new DelegateHandler(async (request, token) =>
        {
            Assert.Equal("application/octet-stream", request.Content!.Headers.ContentType!.MediaType);
            body = await request.Content.ReadAsByteArrayAsync(token);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"text\":\"Привет\"}", Encoding.UTF8, "application/json") };
        });
        var transport = new HttpTranscriptionTransport(new HttpClient(handler));
        var result = await transport.TranscribeAsync(Request([1, 2, 3, 4]), default);
        Assert.Equal("Привет", result.Text);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(body!, 0, 4));
        Assert.Equal(48, body!.Length);
        Assert.Equal(4, BitConverter.ToInt32(body, 40));
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, false)]
    [InlineData(HttpStatusCode.UnprocessableEntity, false)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    public async Task ClassifiesErrors(HttpStatusCode status, bool transient)
    {
        var transport = new HttpTranscriptionTransport(new HttpClient(new DelegateHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(status)))));
        var exception = await Record.ExceptionAsync(() => transport.TranscribeAsync(Request([0, 0]), default));
        Assert.Equal(transient, exception is TransientTranscriptionException);
    }

    [Fact]
    public async Task RejectsMalformedAndEmptyResults()
    {
        var malformed = new HttpTranscriptionTransport(new HttpClient(new DelegateHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("no") }))));
        await Assert.ThrowsAsync<TranscriptionException>(() => malformed.TranscribeAsync(Request([0, 0]), default));
    }

    [Fact]
    public async Task FullRecognitionTimeoutIsNotRetried()
    {
        var transport = new HttpTranscriptionTransport(new HttpClient(new DelegateHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        })));
        var request = Request([0, 0]) with { RecognitionTimeout = TimeSpan.FromMilliseconds(100) };
        var exception = await Record.ExceptionAsync(() => transport.TranscribeAsync(request, default));
        Assert.IsType<TranscriptionException>(exception);
    }

    [Fact]
    public async Task ConnectTimeoutIsTransient()
    {
        // SocketsHttpHandler reports ConnectTimeout as TaskCanceledException wrapping TimeoutException.
        var transport = new HttpTranscriptionTransport(new HttpClient(new DelegateHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("connect", new TimeoutException())))));
        var exception = await Record.ExceptionAsync(() => transport.TranscribeAsync(Request([0, 0]), default));
        Assert.IsType<TransientTranscriptionException>(exception);
    }

    [Fact]
    public async Task TimeoutWhileReadingBodyIsReportedAsErrorNotCancellation()
    {
        var transport = new HttpTranscriptionTransport(new HttpClient(new DelegateHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new NeverEndingStream()) }))));
        var request = Request([0, 0]) with { RecognitionTimeout = TimeSpan.FromMilliseconds(100) };
        var exception = await Record.ExceptionAsync(() => transport.TranscribeAsync(request, default));
        Assert.IsType<TranscriptionException>(exception);
    }

    [Fact]
    public async Task RetryAfterIsCappedByMaximumDelay()
    {
        var attempts = 0;
        var inner = new DelegateTransport(() => ++attempts == 1
            ? throw new TransientTranscriptionException("busy", TimeSpan.FromHours(1))
            : new RecognitionResult("ok"));
        var transport = new RetryingTransport(inner, new GigaDict.Core.Configuration.RetrySettings { InitialDelaySeconds = 0, MaximumDelaySeconds = 0 });
        var result = await transport.TranscribeAsync(Request([0, 0]), default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.WasRetried);
    }

    private sealed class DelegateTransport(Func<RecognitionResult> attempt) : ITranscriptionTransport
    {
        public Task<RecognitionResult> TranscribeAsync(RecognitionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(attempt());
    }

    private sealed class NeverEndingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static RecognitionRequest Request(byte[] pcm) => new(Guid.NewGuid(), pcm, new Uri("http://localhost:9876"), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request, cancellationToken);
    }
}


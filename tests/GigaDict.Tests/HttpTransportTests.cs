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

    private static RecognitionRequest Request(byte[] pcm) => new(Guid.NewGuid(), pcm, new Uri("http://localhost:9876"), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));

    private sealed class DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request, cancellationToken);
    }
}


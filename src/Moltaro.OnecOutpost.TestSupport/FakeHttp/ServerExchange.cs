using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Signing;
using Xunit;

namespace Moltaro.OnecOutpost.TestSupport.FakeHttp;

// One request of the agent to the FakeServer, as it came: its method, address, version and headers, and the exact
// bytes of its body, copied when it came; and the answer the test gives it, once. The answers that the server signs
// are signed for this request, with its kind, its Outpost-Agent and the hash of its body (§3 of docs/protocol.md).
internal sealed class ServerExchange
{
    private const string Json = "application/json";

    private readonly FakeServer _server;

    // Completed once, by the test; its continuations run apart, so that the agent never goes on inside the test's call.
    private readonly TaskCompletionSource<HttpResponseMessage> _answer =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ServerExchange(FakeServer server, HttpRequestMessage request, byte[] body)
    {
        _server = server;
        Method = request.Method;
        Uri = request.RequestUri!;
        Version = request.Version;
        VersionPolicy = request.VersionPolicy;
        ExpectContinue = request.Headers.ExpectContinue;
        ContentType = request.Content?.Headers.ContentType?.ToString();
        ContentLength = request.Content?.Headers.ContentLength;
        Body = body;

        List<(string Name, string Value)> headers = [];

        foreach (KeyValuePair<string, IEnumerable<string>> header in request.Headers)
        {
            headers.AddRange(header.Value.Select(value => (header.Key, value)));
        }

        if (request.Content is not null)
        {
            foreach (KeyValuePair<string, IEnumerable<string>> header in request.Content.Headers)
            {
                headers.AddRange(header.Value.Select(value => (header.Key, value)));
            }
        }

        Headers = headers;
        Name = Uri.AbsolutePath[(Uri.AbsolutePath.LastIndexOf('/') + 1)..];

        if (!OutpostForms.TryReadRequestKind(Name, out OutpostRequestKindEnum kind))
        {
            throw new InvalidOperationException($"The agent sent a request to {Uri}, which names no request of §1.");
        }

        Kind = kind;
    }

    // The name of the request in its path: pair, poll or outcome.
    public string Name { get; }

    public OutpostRequestKindEnum Kind { get; }

    public HttpMethod Method { get; }

    public Uri Uri { get; }

    public Version Version { get; }

    public HttpVersionPolicy VersionPolicy { get; }

    public bool? ExpectContinue { get; }

    public string? ContentType { get; }

    public long? ContentLength { get; }

    // Every header of the request and of its content, one line for each value.
    public IReadOnlyList<(string Name, string Value)> Headers { get; }

    public byte[] Body { get; }

    // The identity of its Outpost-Agent header, which has to be in the form of §2.
    public Guid Agent
    {
        get
        {
            Assert.True(OutpostHeaders.TryParseAgent(Single(OutpostProtocol.AgentHeader), out Guid agent));

            return agent;
        }
    }

    // The bytes of its Outpost-Signature header, which has to be in the form of §2.
    public byte[] Signature
    {
        get
        {
            Assert.True(OutpostHeaders.TryParseSignature(Single(OutpostProtocol.SignatureHeader), out byte[]? signature));

            return signature;
        }
    }

    internal Task<HttpResponseMessage> Answered => _answer.Task;

    // The values of a header, in their order; none when the request has no such header.
    public IReadOnlyList<string> Values(string name)
    {
        return [.. Headers.Where(header => string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase)).Select(header => header.Value)];
    }

    // The one value of a header, which the request has exactly once.
    public string Single(string name)
    {
        return Assert.Single(Values(name));
    }

    // The poll, verified with the agent's key and read as the server reads it (§6.2).
    public OutpostPollRequest ReadPoll()
    {
        return OutpostCodec.ReadPollRequest(AgentKey, Agent, Body, Signature);
    }

    // The outcome request, verified with the agent's key and read as the server reads it (§6.3).
    public OutpostOutcomeRequest ReadOutcome()
    {
        OutpostOutcomeRequest request =
            OutpostCodec.ReadOutcomeRequest(AgentKey, Agent, Body, Signature, out JsonDocument document);

        document.Dispose();

        return request;
    }

    // The exact bytes of the outcome object that the outcome request carries.
    public byte[] OutcomeBytes()
    {
        return ReadOutcome().Outcome.ToArray();
    }

    public void Answer(HttpResponseMessage response)
    {
        Assert.True(_answer.TrySetResult(response), "The request was answered before.");
    }

    // The request ends with an error of the connection, as the handler throws it.
    public void Fail(Exception exception)
    {
        Assert.True(_answer.TrySetException(exception), "The request was answered before.");
    }

    // The request ends as the handler ends it when the proxy answers its CONNECT with 407 (§9).
    public void FailTunnelRefused()
    {
        Fail(new HttpRequestException(
            HttpRequestError.ProxyTunnelError,
            "The proxy tunnel request to proxy 'http://proxy.example.com:3128/' failed with status code '407'.",
            null,
            HttpStatusCode.ProxyAuthenticationRequired));
    }

    // An answer of the library: the status, the body in application/json and its signature for this request.
    public void AnswerSigned(int status, byte[] body)
    {
        Answer(Signed(JsonAnswer(status, body), _server.SignAnswer(this, status, body)));
    }

    // A signed answer whose Content-Type is another than exactly application/json, such as one with a charset.
    public void AnswerSigned(int status, byte[] body, string contentType)
    {
        HttpResponseMessage response = JsonAnswer(status, body);
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

        Answer(Signed(response, _server.SignAnswer(this, status, body)));
    }

    // A verified 200 whose status and headers come and whose body gives its first bytes and then stops: the signature
    // is the server's over the whole body. It gives the body, whose Stalled a test waits for before it moves the clock.
    public StalledStream AnswerStalled(byte[] body)
    {
        StalledStream stalled = new(body[..10]);
        StreamContent content = new(stalled);
        content.Headers.ContentType = new MediaTypeHeaderValue(Json);

        Answer(Signed(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }, _server.SignAnswer(this, 200, body)));

        return stalled;
    }

    // A 200 in application/json with a body made as it is read, its length declared or not, and a signature that no
    // reader reaches.
    public void AnswerStreamed(GeneratedStream body, bool declared)
    {
        StreamContent content = new(body);
        content.Headers.ContentType = new MediaTypeHeaderValue(Json);

        if (declared)
        {
            content.Headers.ContentLength = body.Length;
        }

        Answer(Signed(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }, _server.SignAnswer(this, 200, [])));
    }

    // A redirect, which the agent follows not.
    public void AnswerRedirect()
    {
        HttpResponseMessage response = new(HttpStatusCode.Found);
        response.Headers.Location = new Uri($"https://elsewhere.example.com/agents/v1/{Name}");

        Answer(response);
    }

    // The same, signed with another key than the server's.
    public void AnswerSignedBy(ECDsa key, int status, byte[] body)
    {
        byte[] signature = OutpostSignatures.SignAnswer(key, Kind, Agent, status, Body, body);

        Answer(Signed(JsonAnswer(status, body), signature));
    }

    // The signature of the server for this request over the body, as an answer of this status carries it (§3).
    public byte[] SignatureOf(int status, byte[] body)
    {
        return _server.SignAnswer(this, status, body);
    }

    // The answer with an Outpost-Signature header of these bytes.
    public static HttpResponseMessage Signed(HttpResponseMessage response, byte[] signature)
    {
        response.Headers.TryAddWithoutValidation(OutpostProtocol.SignatureHeader, OutpostHeaders.FormatSignature(signature));

        return response;
    }

    // The verified 200 to a poll.
    public void AnswerPoll(OutpostPollAnswer answer)
    {
        AnswerSigned(200, OutpostCodec.Write(answer));
    }

    // The verified 200 to an outcome request, which confirms its command (§6.3).
    public void Confirm()
    {
        Guid command = ReadOutcome().Command;

        AnswerSigned(200, OutpostCodec.Write(new OutpostOutcomeAnswer(_server.Now(), command)));
    }

    // A signed refusal of §9, such as 400 form.
    public void Refuse(int status, string code)
    {
        AnswerSigned(status, OutpostCodec.Write(new OutpostRefusal(_server.Now(), code)));
    }

    // A status alone, without a body or an Outpost-Signature, as anything on the path can send it.
    public void AnswerBare(int status)
    {
        Answer(new HttpResponseMessage((HttpStatusCode)status));
    }

    // The unsigned answer of §9 for a version the server does not speak.
    public void AnswerUnsupportedVersion()
    {
        Answer(JsonAnswer(400, OutpostCodec.WriteUnsupportedVersion()));
    }

    // A 413 of a hop, such as Kestrel or an ingress: a page of its own, without an Outpost-Signature.
    public void AnswerHtml413()
    {
        HttpResponseMessage response = new(HttpStatusCode.RequestEntityTooLarge)
        {
            Content = new StringContent(
                "<html><head><title>413 Request Entity Too Large</title></head><body>nginx</body></html>",
                Encoding.UTF8,
                "text/html"),
        };

        Answer(response);
    }

    // An answer with the status and a body in application/json, unsigned.
    public static HttpResponseMessage JsonAnswer(int status, byte[] body)
    {
        ByteArrayContent content = new(body);
        content.Headers.ContentType = new MediaTypeHeaderValue(Json);

        return new HttpResponseMessage((HttpStatusCode)status) { Content = content };
    }

    private ECDsa AgentKey =>
        _server.AgentKey ?? throw new InvalidOperationException("The fake server was given no key of the agent.");
}

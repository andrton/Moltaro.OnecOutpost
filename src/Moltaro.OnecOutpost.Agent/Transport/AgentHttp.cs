using System.Net;
using System.Net.Http.Headers;
using System.Net.Mime;
using Moltaro.OnecOutpost.Protocol;

namespace Moltaro.OnecOutpost.Agent.Transport;

// The agent's requests and the answers to them in HTTP (§1, §4, §9 and §10 of docs/protocol.md), for the connection and
// for pairing. A request is a POST over HTTP/1.1 only to {prefix}/v1/{request} of the server's address, with the exact
// bytes that the agent signed, Content-Type application/json, Outpost-Agent, Outpost-Signature, Accept-Encoding
// identity, and on outcome Expect: 100-continue; no Content-Encoding and no credentials. An answer is read within its
// limit and its wait, and nothing of it is trusted here: the readers of the codec verify it.
internal static class AgentHttp
{
    // Decompression is off in the handler, so this is the one coding the agent asks for (§2).
    private const string Identity = "identity";

    // The address of a request: the server's address is the prefix that the application gave MapOutpostAgents, with or
    // without its last slash, and its path is kept.
    public static Uri PathOf(Uri server, OutpostRequestKindEnum kind)
    {
        string prefix = server.AbsoluteUri.EndsWith('/') ? server.AbsoluteUri : server.AbsoluteUri + "/";

        string request = kind switch
        {
            OutpostRequestKindEnum.Pair => "pair",
            OutpostRequestKindEnum.Poll => "poll",
            OutpostRequestKindEnum.Outcome => "outcome",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        return new Uri(new Uri(prefix), $"v{OutpostProtocol.Version}/{request}");
    }

    // The request of the kind to the address, carrying the body as it was signed and the signature of the agent's key
    // over it, under the identity given: the agent's, or in pair the one that the token names.
    public static HttpRequestMessage Request(
        Uri address,
        OutpostRequestKindEnum kind,
        Guid agent,
        byte[] body,
        byte[] signature)
    {
        ByteArrayContent content = new(body);
        content.Headers.ContentType = new MediaTypeHeaderValue(MediaTypeNames.Application.Json);

        HttpRequestMessage request = new(HttpMethod.Post, address)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = content,
        };

        request.Headers.Add(OutpostProtocol.AgentHeader, OutpostHeaders.FormatAgent(agent));
        request.Headers.Add(OutpostProtocol.SignatureHeader, OutpostHeaders.FormatSignature(signature));
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue(Identity));

        // So that a refusal for the length comes before the body is sent (§9); the handler sends the body anyway after
        // the second of .NET that X2 of the register names.
        if (kind == OutpostRequestKindEnum.Outcome)
        {
            request.Headers.ExpectContinue = true;
        }

        return request;
    }

    // Sends the request and gives its answer once its status and headers came, before the wait ends. Without an answer
    // it gives null, with the status of a proxy that refused the tunnel, such as 407, since that status came (§9), or
    // with none: an error of the connection, or the end of the wait. The cancellation of cancellationToken, which the
    // wait includes, passes.
    public static async Task<(HttpResponseMessage? Response, int? Status)> SendAsync(
        HttpMessageInvoker invoker,
        HttpRequestMessage request,
        CancellationToken wait,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await invoker.SendAsync(request, wait), null);
        }
        catch (HttpRequestException exception)
        {
            return (null, (int?)exception.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, null);
        }
    }

    // Reads the body of an answer within the limit before the wait ends. A declared length beyond the limit fails before
    // a byte is read, and a body without one fails at its first byte beyond it; such a body, one cut by an error of the
    // connection and one that is late give a null body, and the handler reads nothing more of them. The cancellation of
    // cancellationToken, which the wait includes, passes.
    public static async Task<ServerAnswer> ReadAsync(
        HttpResponseMessage response,
        int limit,
        CancellationToken wait,
        CancellationToken cancellationToken)
    {
        byte[]? body;

        try
        {
            await response.Content.LoadIntoBufferAsync(limit, wait);
            body = await response.Content.ReadAsByteArrayAsync(wait);
        }
        catch (HttpRequestException)
        {
            body = null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            body = null;
        }

        // Exactly application/json: a charset or another parameter makes another media type of §4.
        bool isJson = response.Content.Headers.ContentType?.ToString() == MediaTypeNames.Application.Json;

        return new ServerAnswer((int)response.StatusCode, body, isJson, SignatureOf(response));
    }

    // The bytes of the one Outpost-Signature of the answer in the form of §2; empty, which verifies nothing, for none,
    // for more than one, and for any other form.
    private static byte[] SignatureOf(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(OutpostProtocol.SignatureHeader, out IEnumerable<string>? values))
        {
            return [];
        }

        string[] given = [.. values];

        return given.Length == 1 && OutpostHeaders.TryParseSignature(given[0], out byte[]? signature) ? signature : [];
    }
}

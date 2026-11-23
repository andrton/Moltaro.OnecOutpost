using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Moltaro.OnecOutpost.Protocol;

namespace Moltaro.OnecOutpost.TestSupport.Loopback;

// The HTTP proxy of an agent's network, on loopback, for the tests of the agent: a TcpListener on port 0 of 127.0.0.1
// that answers each CONNECT with 200, ends TLS for outpost.test with a certificate made for the test run, and relays the
// request inside to a LoopbackServer: the one of Node, or the one at the port of 127.0.0.1 that the CONNECT names. So it
// opens TLS, as a hop on the path can (concept, "What protects what"), and the agent's checks of the signatures are
// what keeps the tunnel honest. The agent reaches it with the address of ServerAddress, a name of RFC 6761 that nobody
// resolves: the proxy alone takes the name.
//
// It reads each request whole, records it in Requests, and forwards it over HTTP, unless a rule that a test set takes
// it: a rule drops the request, drops its answer, or holds it until the test releases it (§13 of docs/protocol.md,
// "What a hop on the path can still do"). It writes back the answer of the node with what the agent reads of one, and
// a test can replay any request it recorded.
//
// Asked to, it refuses every CONNECT with 407 and the schemes Negotiate and NTLM, as a proxy that asks for the
// computer's credentials does, and keeps the connection open for another CONNECT. It records the head line of every
// CONNECT and every Proxy-Authorization it is sent. A connection that breaks ends quietly: the test sees the effect at
// the agent.
internal sealed partial class TestProxy : IAsyncDisposable
{
    // The name of the server's address in the tests of the agent.
    public const string Host = "outpost.test";

    // A head of a CONNECT or of a request longer than this is none of the agent's.
    private const int HeadLimit = 64 * 1024;

    private static readonly byte[] Established = "HTTP/1.1 200 Connection established\r\n\r\n"u8.ToArray();

    private static readonly byte[] Continue = "HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray();

    private static readonly byte[] AuthenticationRequired = Encoding.ASCII.GetBytes(
        "HTTP/1.1 407 Proxy Authentication Required\r\n"
        + "Proxy-Authenticate: Negotiate\r\n"
        + "Proxy-Authenticate: NTLM\r\n"
        + "Content-Length: 0\r\n"
        + "\r\n");

    // The header lines of one hop, which the proxy does not forward: it frames each request and each answer itself.
    private static readonly HashSet<string> HopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host",
        "Content-Length",
        "Expect",
        "Connection",
        "Keep-Alive",
        "Proxy-Connection",
        "TE",
        "Transfer-Encoding",
        "Upgrade",
    };

    // One certificate for outpost.test serves every proxy of the test run.
    private static readonly Lazy<X509Certificate2> Certificate = new(CreateCertificate);

    private readonly TcpListener _listener;

    private readonly CancellationTokenSource _stop = new();

    // The client through which requests reach the nodes. Every request asks for a connection of its own, so that the
    // client never sends one again by itself, as .NET can on a pooled connection that closed before any answer came.
    private readonly HttpClient _nodes = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        UseProxy = false,
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    // Guards the lists, the flag and the node. No I/O happens under it; the predicates of the rules, which only read a
    // request, are called under it.
    private readonly Lock _lock = new();

    private readonly List<string> _connects = [];

    private readonly List<string> _authorizations = [];

    private readonly List<ProxiedRequest> _requests = [];

    // The rules that took no request yet, in the order the test set them.
    private readonly List<Rule> _rules = [];

    private readonly List<Task> _connections = [];

    private readonly Task _accepting;

    private bool _asksForCredentials;

    private LoopbackServer? _node;

    private TestProxy(TcpListener listener)
    {
        _listener = listener;
        Uri = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
        _accepting = AcceptAsync(_stop.Token);
    }

    // The address that policy.json names as the proxy.
    public Uri Uri { get; }

    // When true, every CONNECT from then on is refused with 407 and Negotiate and NTLM.
    public bool AsksForCredentials
    {
        get
        {
            lock (_lock)
            {
                return _asksForCredentials;
            }
        }

        set
        {
            lock (_lock)
            {
                _asksForCredentials = value;
            }
        }
    }

    // The node that every request read from then on goes to, as a balancer in front of several nodes sends it, whatever
    // port the CONNECT names; while null, the node at that port of 127.0.0.1.
    public LoopbackServer? Node
    {
        get
        {
            lock (_lock)
            {
                return _node;
            }
        }

        set
        {
            lock (_lock)
            {
                _node = value;
            }
        }
    }

    // The head line of every CONNECT, such as "CONNECT outpost.test:5000 HTTP/1.1", in the order they came.
    public IReadOnlyList<string> Connects
    {
        get
        {
            lock (_lock)
            {
                return [.. _connects];
            }
        }
    }

    // The value of every Proxy-Authorization of a CONNECT, in the order they came.
    public IReadOnlyList<string> Authorizations
    {
        get
        {
            lock (_lock)
            {
                return [.. _authorizations];
            }
        }
    }

    // Every request of the agent that the proxy read, in the order it read them, whatever was done with it: forwarded,
    // dropped or held. A captured request of §13 is one of these.
    public IReadOnlyList<ProxiedRequest> Requests
    {
        get
        {
            lock (_lock)
            {
                return [.. _requests];
            }
        }
    }

    public static TestProxy Start()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        return new TestProxy(listener);
    }

    // The address that agent.json names for the node: the endpoints of the node by the name that only this proxy takes,
    // at the port where the node listens, which the CONNECT names.
    public static Uri ServerAddress(LoopbackServer server)
    {
        return new Uri($"https://{Host}:{server.Address.Port}{LoopbackServer.Prefix}/");
    }

    // The validation of the server's certificate that a test gives the agent's handler in place of the system's: it
    // accepts the certificate of the proxy and nothing else.
    public static bool ValidateCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors errors)
    {
        return certificate is not null
            && certificate.GetCertHashString() == Certificate.Value.GetCertHashString();
    }

    // Drops the next request read from now on that match accepts: it reaches no node, and the proxy closes the agent's
    // connection without an answer, which the agent sees as an error of the connection. Completes with the request once
    // it was dropped. A test sets the rule before it lets the agent send the request.
    public async Task<ProxiedRequest> DropNextRequestAsync(
        Func<ProxiedRequest, bool> match,
        CancellationToken cancellationToken)
    {
        return (ProxiedRequest)await SetAsync(LossEnum.DropRequest, match, cancellationToken);
    }

    // Drops the answer to the next request read from now on that match accepts: the request reaches the node, which
    // handles it, and the proxy closes the agent's connection in place of the answer. Completes with the node's answer
    // once it was dropped, and fails when the node gave none, as when it stopped.
    public async Task<AgentAnswer> DropNextAnswerAsync(
        Func<ProxiedRequest, bool> match,
        CancellationToken cancellationToken)
    {
        return (AgentAnswer)await SetAsync(LossEnum.DropAnswer, match, cancellationToken);
    }

    // Holds the next request read from now on that match accepts: it reaches no node until the test releases it, while
    // the agent's connection stays open. Completes with the held request once it is held.
    public async Task<HeldRequest> HoldNextAsync(Func<ProxiedRequest, bool> match, CancellationToken cancellationToken)
    {
        return (HeldRequest)await SetAsync(LossEnum.Hold, match, cancellationToken);
    }

    // Sends a request again to the node given, with the exact bytes of its body and its header lines, as a hop that
    // captured it replays it, and gives the answer, which reaches the test alone. An error of the connection to the
    // node fails it.
    public async Task<AgentAnswer> ReplayAsync(
        ProxiedRequest request,
        LoopbackServer node,
        CancellationToken cancellationToken)
    {
        return await SendAsync(request, node.Address, cancellationToken).WaitAsync(LoopbackServer.Deadline, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();

        await _accepting.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        Task[] connections;

        lock (_lock)
        {
            connections = [.. _connections];
        }

        await Task.WhenAll(connections).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        _nodes.Dispose();
        _stop.Dispose();
    }

    // A certificate for outpost.test, signed by itself. It is loaded again from its PKCS#12 form, since TLS on Windows
    // takes no key that lives only in memory.
    private static X509Certificate2 CreateCertificate()
    {
        using RSA key = RSA.Create(2048);

        CertificateRequest request = new($"CN={Host}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        SubjectAlternativeNameBuilder names = new();
        names.AddDnsName(Host);
        request.CertificateExtensions.Add(names.Build());

        DateTimeOffset now = DateTimeOffset.UtcNow;

        using X509Certificate2 made = request.CreateSelfSigned(now.AddDays(-1), now.AddDays(1));

        return X509CertificateLoader.LoadPkcs12(made.Export(X509ContentType.Pkcs12), null);
    }

    // The kind of a request by the last segment of its path, {prefix}/v1/{request} (§1 of docs/protocol.md). A path of
    // no request of the agent ends the tunnel: the agent sends none.
    private static OutpostRequestKindEnum KindOf(string path)
    {
        if (!OutpostForms.TryReadRequestKind(path[(path.LastIndexOf('/') + 1)..], out OutpostRequestKindEnum kind))
        {
            throw new IOException($"The path {path} names no request of the agent.");
        }

        return kind;
    }

    // Writes what the agent reads of an answer, its status, Content-Type, Outpost-Signature and body, framed by
    // Content-Length, with Connection: close.
    private static async Task WriteAnswerAsync(Stream agent, AgentAnswer answer, CancellationToken cancellationToken)
    {
        StringBuilder head = new();
        head.Append(CultureInfo.InvariantCulture, $"HTTP/1.1 {answer.Status} {ReasonPhrases.GetReasonPhrase(answer.Status)}\r\n");

        if (answer.ContentType is not null)
        {
            head.Append(CultureInfo.InvariantCulture, $"Content-Type: {answer.ContentType}\r\n");
        }

        if (answer.Signature is not null)
        {
            head.Append(CultureInfo.InvariantCulture, $"{OutpostProtocol.SignatureHeader}: {answer.Signature}\r\n");
        }

        head.Append(CultureInfo.InvariantCulture, $"Content-Length: {answer.Body.Length}\r\n");
        head.Append("Connection: close\r\n\r\n");

        await agent.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), cancellationToken);
        await agent.WriteAsync(answer.Body, cancellationToken);
        await agent.FlushAsync(cancellationToken);
    }

    // The head of a CONNECT or of a request, read byte by byte to its empty line, so that nothing of the bytes after it
    // is taken; null when the connection closed before a byte of it came.
    private static async Task<string?> ReadHeadAsync(Stream stream, CancellationToken cancellationToken)
    {
        List<byte> head = [];
        byte[] one = new byte[1];

        while (true)
        {
            int read = await stream.ReadAsync(one, cancellationToken);

            if (read == 0)
            {
                return head.Count == 0 ? null : throw new IOException("The connection closed inside a head.");
            }

            head.Add(one[0]);

            if (head.Count > HeadLimit)
            {
                throw new IOException("The head is longer than any of the agent.");
            }

            if (head.Count >= 4 && head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n')
            {
                return Encoding.ASCII.GetString([.. head], 0, head.Count - 4);
            }
        }
    }

    // Sets a rule, at once, and gives the task that completes when it took effect, within the deadline of every wait of
    // a test.
    private Task<object> SetAsync(LossEnum loss, Func<ProxiedRequest, bool> match, CancellationToken cancellationToken)
    {
        Rule rule = new(loss, match);

        lock (_lock)
        {
            _rules.Add(rule);
        }

        return rule.Met.Task.WaitAsync(LoopbackServer.Deadline, cancellationToken);
    }

    private async Task AcceptAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            lock (_lock)
            {
                _connections.Add(ServeAsync(client, cancellationToken));
            }
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        // Off the accepting loop, so that one connection never holds up the next.
        await Task.Yield();

        using (client)
        {
            try
            {
                await TunnelAsync(client.GetStream(), cancellationToken);
            }
            catch (Exception exception) when (exception is IOException
                or SocketException
                or AuthenticationException
                or OperationCanceledException
                or ObjectDisposedException)
            {
                // The connection broke or the proxy stopped.
            }
        }
    }

    private async Task TunnelAsync(NetworkStream agent, CancellationToken cancellationToken)
    {
        while (true)
        {
            string? head = await ReadHeadAsync(agent, cancellationToken);

            if (head is null)
            {
                return;
            }

            string[] lines = head.Split("\r\n");
            bool refuses = Record(lines);

            if (refuses)
            {
                await agent.WriteAsync(AuthenticationRequired, cancellationToken);

                continue;
            }

            // CONNECT outpost.test:{port} HTTP/1.1: the node listens on that port of 127.0.0.1.
            string target = lines[0].Split(' ')[1];
            int port = int.Parse(target[(target.LastIndexOf(':') + 1)..], CultureInfo.InvariantCulture);

            await agent.WriteAsync(Established, cancellationToken);
            await RelayAsync(agent, port, cancellationToken);

            return;
        }
    }

    // Ends TLS for the agent, reads its request, does with it what the rule that takes it says or forwards it, and
    // writes the answer back with Connection: close. So every request comes on a tunnel of its own: on a connection it
    // reused, the agent's handler could send a request again by itself when the proxy closes it unanswered, and a
    // dropped request would reach a node after all.
    private async Task RelayAsync(NetworkStream agent, int port, CancellationToken cancellationToken)
    {
        SslStream tls = new(agent, leaveInnerStreamOpen: true);
        Task? closed = null;

        try
        {
            SslServerAuthenticationOptions options = new()
            {
                ServerCertificate = Certificate.Value,
                ApplicationProtocols = [SslApplicationProtocol.Http11],
            };

            await tls.AuthenticateAsServerAsync(options, cancellationToken);

            ProxiedRequest? request = await ReadRequestAsync(tls, cancellationToken);

            if (request is null)
            {
                return;
            }

            Uri node = Node?.Address ?? new Uri($"http://127.0.0.1:{port}/");
            Rule? rule = Take(request);

            // The agent sends nothing more on the connection before its answer, so this read ends only when the agent
            // closes the connection, as when its wait ends or it stops, or when the proxy stops.
            closed = tls.ReadAsync(new byte[1], cancellationToken).AsTask();

            AgentAnswer? answer = await AnswerAsync(request, rule, node, closed, cancellationToken);

            if (answer is not null)
            {
                await WriteAnswerAsync(tls, answer, cancellationToken);
            }
        }
        finally
        {
            await tls.DisposeAsync();
            agent.Close();

            if (closed is not null)
            {
                await closed.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    // Reads a request of the agent whole: its head, then its body by Content-Length, which the agent always declares.
    // To Expect: 100-continue it answers 100 Continue itself, as a hop that reads a body whole before it forwards it
    // does. Records the request. Null when the connection closed before a byte of it came.
    private async Task<ProxiedRequest?> ReadRequestAsync(Stream agent, CancellationToken cancellationToken)
    {
        string? head = await ReadHeadAsync(agent, cancellationToken);

        if (head is null)
        {
            return null;
        }

        string[] lines = head.Split("\r\n");

        // POST {prefix}/v1/{request} HTTP/1.1
        string path = lines[0].Split(' ')[1];
        List<(string Name, string Value)> headers = [];
        int length = 0;
        bool expectsContinue = false;

        foreach (string line in lines.Skip(1))
        {
            int colon = line.IndexOf(':', StringComparison.Ordinal);
            string name = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            headers.Add((name, value));

            if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(value, CultureInfo.InvariantCulture);
            }

            if (string.Equals(name, "Expect", StringComparison.OrdinalIgnoreCase))
            {
                expectsContinue = string.Equals(value, "100-continue", StringComparison.OrdinalIgnoreCase);
            }
        }

        if (expectsContinue)
        {
            await agent.WriteAsync(Continue, cancellationToken);
        }

        byte[] body = new byte[length];
        await agent.ReadExactlyAsync(body, cancellationToken);

        ProxiedRequest request = new(KindOf(path), path, headers, body);

        lock (_lock)
        {
            _requests.Add(request);
        }

        return request;
    }

    // The first rule, in the order they were set, that accepts the request; it takes this request alone.
    private Rule? Take(ProxiedRequest request)
    {
        lock (_lock)
        {
            Rule? rule = _rules.FirstOrDefault(candidate => candidate.Match(request));

            if (rule is not null)
            {
                _rules.Remove(rule);
            }

            return rule;
        }
    }

    // What the agent gets for the request: the node's answer, or null for none, when a rule dropped the request or the
    // answer, when the node gave none, and when the agent closed its connection first.
    private async Task<AgentAnswer?> AnswerAsync(
        ProxiedRequest request,
        Rule? rule,
        Uri node,
        Task closed,
        CancellationToken cancellationToken)
    {
        switch (rule?.Loss)
        {
            case LossEnum.DropRequest:
                rule.Met.TrySetResult(request);

                return null;

            case LossEnum.Hold:
                HeldRequest held = new(this, request);
                rule.Met.TrySetResult(held);

                // An agent that gave up has closed the connection, and a later release reaches the test alone.
                await Task.WhenAny(held.Answer, closed);

                return held.Answer.IsCompletedSuccessfully ? held.Answer.Result : null;

            case LossEnum.DropAnswer:
                AgentAnswer? dropped = await ForwardAsync(request, node, closed, cancellationToken);

                if (dropped is null)
                {
                    rule.Met.TrySetException(new IOException("The node gave no answer to drop."));
                }
                else
                {
                    rule.Met.TrySetResult(dropped);
                }

                return null;

            default:
                return await ForwardAsync(request, node, closed, cancellationToken);
        }
    }

    // Sends the request to the node and gives its answer; null when the node gave none, as when it stopped, and when
    // the agent closed its connection first, which ends the request at the node too, as a hop does.
    private async Task<AgentAnswer?> ForwardAsync(
        ProxiedRequest request,
        Uri node,
        Task closed,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource abort = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<AgentAnswer> forward = SendAsync(request, node, abort.Token);

        if (await Task.WhenAny(forward, closed) != forward)
        {
            abort.Cancel();
        }

        try
        {
            return await forward;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            return null;
        }
    }

    // Sends the request to the node whose endpoints are at the address given, by the path of its head line, with its
    // header lines but those of one hop and the exact bytes of its body, and reads the answer whole.
    private async Task<AgentAnswer> SendAsync(ProxiedRequest request, Uri node, CancellationToken cancellationToken)
    {
        using HttpRequestMessage message = new(HttpMethod.Post, new Uri(node, request.Path))
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new ByteArrayContent(request.Body),
        };

        message.Headers.ConnectionClose = true;

        foreach ((string name, string value) in request.Headers)
        {
            if (HopHeaders.Contains(name))
            {
                continue;
            }

            // Content-Type is a header of the content; the others are of the request.
            if (!message.Headers.TryAddWithoutValidation(name, value))
            {
                message.Content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        using HttpResponseMessage response = await _nodes.SendAsync(message, cancellationToken);
        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        string? signature = null;

        if (response.Headers.TryGetValues(OutpostProtocol.SignatureHeader, out IEnumerable<string>? values))
        {
            signature = values.Single();
        }

        return new AgentAnswer(
            request.Kind,
            (int)response.StatusCode,
            response.Content.Headers.ContentType?.ToString(),
            signature,
            body,
            request.Body,
            request.Signature);
    }

    // Records the head line and every Proxy-Authorization of a CONNECT, and gives whether it is refused.
    private bool Record(string[] lines)
    {
        const string Authorization = "Proxy-Authorization:";

        lock (_lock)
        {
            _connects.Add(lines[0]);

            foreach (string line in lines.Skip(1))
            {
                if (line.StartsWith(Authorization, StringComparison.OrdinalIgnoreCase))
                {
                    _authorizations.Add(line[Authorization.Length..].Trim());
                }
            }

            return _asksForCredentials;
        }
    }
}

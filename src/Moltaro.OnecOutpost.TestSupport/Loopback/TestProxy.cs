using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Moltaro.OnecOutpost.TestSupport.Loopback;

// The HTTP proxy of an agent's network, on loopback, for the tests of the agent: a TcpListener on port 0 of 127.0.0.1
// that answers each CONNECT with 200, ends TLS for outpost.test with a certificate made for the test run, and pipes the
// plain bytes to the port that the CONNECT names on 127.0.0.1, where a LoopbackServer listens. So it opens TLS, as a
// hop on the path can (concept, "What protects what"), and the agent's checks of the signatures are what keeps the
// tunnel honest. The agent reaches it with the address of ServerAddress, a name of RFC 6761 that nobody resolves: the
// proxy alone takes the name.
//
// Asked to, it refuses every CONNECT with 407 and the schemes Negotiate and NTLM, as a proxy that asks for the
// computer's credentials does, and keeps the connection open for another CONNECT. It records the head line of every
// CONNECT and every Proxy-Authorization it is sent. A connection that breaks ends quietly: the test sees the effect at
// the agent.
internal sealed class TestProxy : IAsyncDisposable
{
    // The name of the server's address in the tests of the agent.
    public const string Host = "outpost.test";

    // A head of a CONNECT longer than this is no CONNECT of the agent.
    private const int HeadLimit = 64 * 1024;

    private static readonly byte[] Established = "HTTP/1.1 200 Connection established\r\n\r\n"u8.ToArray();

    private static readonly byte[] AuthenticationRequired = Encoding.ASCII.GetBytes(
        "HTTP/1.1 407 Proxy Authentication Required\r\n"
        + "Proxy-Authenticate: Negotiate\r\n"
        + "Proxy-Authenticate: NTLM\r\n"
        + "Content-Length: 0\r\n"
        + "\r\n");

    // One certificate for outpost.test serves every proxy of the test run.
    private static readonly Lazy<X509Certificate2> Certificate = new(CreateCertificate);

    private readonly TcpListener _listener;

    private readonly CancellationTokenSource _stop = new();

    // Guards the lists and the flag. No I/O happens under it.
    private readonly Lock _lock = new();

    private readonly List<string> _connects = [];

    private readonly List<string> _authorizations = [];

    private readonly List<Task> _connections = [];

    private readonly Task _accepting;

    private bool _asksForCredentials;

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
            await PipeAsync(agent, port, cancellationToken);

            return;
        }
    }

    // Ends TLS for the agent and pipes the plain bytes to the node and back, until either side closes.
    private static async Task PipeAsync(NetworkStream agent, int port, CancellationToken cancellationToken)
    {
        await using SslStream tls = new(agent, leaveInnerStreamOpen: true);

        SslServerAuthenticationOptions options = new()
        {
            ServerCertificate = Certificate.Value,
            ApplicationProtocols = [SslApplicationProtocol.Http11],
        };

        await tls.AuthenticateAsServerAsync(options, cancellationToken);

        using TcpClient node = new();
        await node.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
        NetworkStream origin = node.GetStream();

        using CancellationTokenSource closed = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task upstream = tls.CopyToAsync(origin, closed.Token);
        Task downstream = origin.CopyToAsync(tls, closed.Token);

        // A side that closes ends the tunnel, as a proxy closes both connections of a tunnel.
        await Task.WhenAny(upstream, downstream);
        closed.Cancel();
        node.Close();
        agent.Close();

        await Task.WhenAll(upstream, downstream).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    // The head of a request, read byte by byte to its empty line, so that nothing of the bytes after it is taken; null
    // when the connection closed before a byte of it came.
    private static async Task<string?> ReadHeadAsync(NetworkStream stream, CancellationToken cancellationToken)
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
                throw new IOException("The head is longer than any CONNECT of the agent.");
            }

            if (head.Count >= 4 && head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n')
            {
                return Encoding.ASCII.GetString([.. head], 0, head.Count - 4);
            }
        }
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

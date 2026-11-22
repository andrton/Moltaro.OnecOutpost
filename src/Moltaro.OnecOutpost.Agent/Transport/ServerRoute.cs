using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Moltaro.OnecOutpost.Agent.Transport;

// The way to the server: which addresses the agent reaches (§11 of docs/protocol.md) and the handler it reaches them
// through, with the proxy of the policy's proxy row.
internal static class ServerRoute
{
    private const string Localhost = "localhost";

    // How Uri writes the one IPv6 loopback address, whatever form the address gave it in.
    private const string IPv6Loopback = "[::1]";

    // Accepts an absolute https address, and an http one whose Uri.Host is localhost, an address in 127.0.0.0/8 with
    // HostNameType IPv4, or [::1] with HostNameType IPv6; nothing is resolved. So loopback, 127.1 and 2130706433 count,
    // and localhost. and foo.localhost do not: Uri normalises before anyone reads the host, each form it maps there is
    // loopback, and one rule on its result keeps §11.
    public static bool IsAllowed(Uri server)
    {
        if (!server.IsAbsoluteUri)
        {
            return false;
        }

        if (server.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        if (server.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        return server.HostNameType switch
        {
            UriHostNameType.IPv4 => IPAddress.Parse(server.Host).GetAddressBytes()[0] == 127,
            UriHostNameType.IPv6 => server.Host == IPv6Loopback,
            _ => server.Host == Localhost,
        };
    }

    // Gives a SocketsHttpHandler with AllowAutoRedirect false, no decompression, MaxResponseDrainSize 0, no cookies, no
    // check of revocation, the 1-second Expect100ContinueTimeout of .NET (X2 of the register), UseProxy only for an
    // https server with a proxy named, so never the system's, and no credentials for the proxy or the server, so never
    // NTLM. Throws ArgumentException for an address that IsAllowed refuses.
    public static SocketsHttpHandler CreateHandler(Uri server, Uri? proxy)
    {
        if (!IsAllowed(server))
        {
            throw new ArgumentException(
                "The address of the server is neither https nor http on a loopback address (§11 of the protocol).",
                nameof(server));
        }

        // An http address is never reached through the proxy (§11). Without a proxy named, UseProxy stays false, since
        // a handler that used a proxy without one would take the system's, from WinINet or the environment.
        bool throughProxy = proxy is not null && server.Scheme == Uri.UriSchemeHttps;

        // The proxy and the server are offered no credentials: Credentials, DefaultProxyCredentials and the proxy's own
        // stay null, so a proxy that asks for authentication answers 407 and gets nothing. MaxResponseDrainSize 0 closes
        // the connection of an answer that is not read to its end, so that a body beyond its limit is not read either
        // (§10). The certificate is validated as the system does, without a check of revocation (§11).
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            MaxResponseDrainSize = 0,
            UseCookies = false,
            UseProxy = throughProxy,
            Proxy = throughProxy ? new WebProxy(proxy) : null,
            SslOptions = new SslClientAuthenticationOptions
            {
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            },
        };
    }
}

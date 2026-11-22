using System.Net;
using System.Security.Cryptography.X509Certificates;
using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.Transport;

// The way to the server (§11 of docs/protocol.md, the proxy row of policy.json, the S6 brief, "S6b and S6c"): http://
// only on a loopback address as .NET's Uri writes its host, judged without resolving a name, and never through the
// proxy; every other address https://, validated as the system validates it, without a check of revocation. The
// handler follows no redirect, decompresses nothing, drains nothing, keeps no cookies, waits the one second of .NET for
// 100 Continue, and offers no credentials to the proxy or the server, so never NTLM; it uses the proxy of the policy,
// and never the system's.
public sealed class ServerRouteTests
{
    private static readonly Uri Proxy = new("http://proxy.example.com:3128");

    // Uri writes loopback as localhost, and 127.1, 0x7f000001 and 2130706433 as 127.0.0.1, before anyone reads the host.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("https://outpost.example.com/agents")]
    [InlineData("https://203.0.113.7:8443/agents/")]
    [InlineData("http://localhost:5000/agents")]
    [InlineData("http://LOCALHOST/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://127.255.255.254:8080/agents")]
    [InlineData("http://[::1]:8080/")]
    [InlineData("http://[0:0:0:0:0:0:0:1]/")]
    [InlineData("http://loopback/")]
    [InlineData("http://127.1/")]
    [InlineData("http://0x7f000001/")]
    [InlineData("http://2130706433/")]
    public void An_https_address_and_an_http_address_whose_host_Uri_writes_as_loopback_are_allowed(string address)
    {
        Assert.True(ServerRoute.IsAllowed(new Uri(address)));
    }

    // localhost. and foo.localhost may resolve to loopback, but nothing is resolved; an IPv4 address mapped into IPv6 is
    // not written as ::1.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("http://outpost.example.com/agents")]
    [InlineData("http://localhost./")]
    [InlineData("http://foo.localhost/")]
    [InlineData("http://128.0.0.1/")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://[::2]/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("ftp://outpost.example.com/agents")]
    [InlineData("ws://localhost/")]
    public void Any_other_address_is_refused(string address)
    {
        Assert.False(ServerRoute.IsAllowed(new Uri(address)));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_handler_for_an_address_that_is_refused_is_refused_with_ArgumentException()
    {
        Assert.ThrowsAny<ArgumentException>(
            () => ServerRoute.CreateHandler(new Uri("http://outpost.example.com/agents"), null));
    }

    // The certificate is validated as the system does, never by a callback of the agent's own; the guarantees rest on the
    // pinned server key, not on the certificate (§11).
    [Fact(Skip = SkipReason.NotImplemented)]
    public void The_handler_follows_no_redirect_decompresses_nothing_drains_nothing_keeps_no_cookies_and_checks_no_revocation()
    {
        using SocketsHttpHandler handler = ServerRoute.CreateHandler(new Uri("https://outpost.example.com/agents"), Proxy);

        Assert.False(handler.AllowAutoRedirect);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.Equal(0, handler.MaxResponseDrainSize);
        Assert.False(handler.UseCookies);
        Assert.Equal(X509RevocationMode.NoCheck, handler.SslOptions.CertificateRevocationCheckMode);
        Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
        Assert.Equal(TimeSpan.FromSeconds(1), handler.Expect100ContinueTimeout);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void An_https_server_with_a_proxy_named_is_reached_through_that_proxy_offered_no_credentials()
    {
        Uri server = new("https://outpost.example.com/agents");

        using SocketsHttpHandler handler = ServerRoute.CreateHandler(server, Proxy);

        Assert.True(handler.UseProxy);
        IWebProxy proxy = Assert.IsAssignableFrom<IWebProxy>(handler.Proxy);
        Assert.False(proxy.IsBypassed(server));
        Assert.Equal(Proxy, proxy.GetProxy(server));
        Assert.Null(proxy.Credentials);
        Assert.Null(handler.DefaultProxyCredentials);
        Assert.Null(handler.Credentials);
        Assert.False(handler.PreAuthenticate);
    }

    // A handler that used a proxy without one named would take the system's, from WinINet or the environment.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("https://outpost.example.com/agents", null)]
    [InlineData("http://127.0.0.1:5000/agents", "http://proxy.example.com:3128")]
    [InlineData("http://localhost:5000/agents", null)]
    public void Without_a_proxy_named_and_for_an_http_address_the_handler_uses_no_proxy_not_even_the_systems(
        string server,
        string? proxy)
    {
        using SocketsHttpHandler handler = ServerRoute.CreateHandler(new Uri(server), proxy is null ? null : new Uri(proxy));

        Assert.False(handler.UseProxy);
        Assert.Null(handler.DefaultProxyCredentials);
        Assert.Null(handler.Credentials);
    }
}

using System.Security.Cryptography;
using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Loopback;

namespace Moltaro.OnecOutpost.Agent.Tests.Transport;

// The proxy row of policy.json and §11 of docs/protocol.md, over loopback with the handler of the program: the agent
// offers its proxy no credentials, by no scheme, so never NTLM. A proxy that asks for authentication answers 407, and
// the poll is lost with status-407, the status of the proxy's answer to CONNECT (§9).
public sealed class ProxyTests
{
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_proxy_is_offered_no_credentials_and_a_challenge_of_Negotiate_and_NTLM_gets_none_so_the_poll_is_lost_with_status_407()
    {
        await using TestProxy proxy = TestProxy.Start();
        proxy.AsksForCredentials = true;
        Uri server = new($"https://{TestProxy.Host}:5000/outpost/");
        FakeClock clock = new(TestTimes.Start);
        using ECDsa agentKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using AgentConnection connection = new(
            server,
            PairedFolder.Agent,
            agentKey,
            serverKey,
            ServerRoute.CreateHandler(server, proxy.Uri),
            clock);
        OutpostPollRequest poll = new(7, clock.GetUtcNow(), PairedFolder.Version, TimeSpan.FromSeconds(25), [], []);

        PollResult result = await connection
            .PollAsync(poll, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)
            .WaitAsync(TestTimes.Deadline, TestContext.Current.CancellationToken);

        Assert.Null(result.Answer);
        Assert.Equal("status-407", result.Reason);
        Assert.NotEmpty(proxy.Connects);
        Assert.All(proxy.Connects, connect => Assert.Equal($"CONNECT {TestProxy.Host}:5000 HTTP/1.1", connect));
        Assert.Empty(proxy.Authorizations);
    }
}

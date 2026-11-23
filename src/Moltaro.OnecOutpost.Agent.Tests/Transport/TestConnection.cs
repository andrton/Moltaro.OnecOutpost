using System.Security.Cryptography;
using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeHttp;

namespace Moltaro.OnecOutpost.Agent.Tests.Transport;

// The connection of the agent of PairedFolder to the FakeServer, with its key, the server key it pinned and the fake
// clock at TestTimes.Start. Its waits are those of a policy whose requestTimeout is 10 seconds, the least it allows,
// and of agent.json, a poll timeout of 25 seconds and a lease grace of 10: so a test tells the wait for an answer to
// begin, 35 seconds, from the wait for its body, 10.
internal sealed class TestConnection : IDisposable
{
    public static readonly Uri Address = new("https://outpost.example.com/agents/");

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan RunLimit = TimeSpan.FromMinutes(10);

    private readonly ECDsa _serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private readonly ECDsa _pinnedKey;

    public TestConnection()
        : this(Address)
    {
    }

    public TestConnection(Uri address)
    {
        _pinnedKey = OutpostKeys.ImportPublicKey(_serverKey.ExportSubjectPublicKeyInfo());
        Server = new FakeServer(_serverKey, Clock) { AgentKey = AgentKey };
        Connection = new AgentConnection(address, PairedFolder.Agent, AgentKey, _pinnedKey, Server, Clock);
    }

    public FakeClock Clock { get; } = new(TestTimes.Start);

    // The agent's key, with which it signs its requests.
    public ECDsa AgentKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public FakeServer Server { get; }

    public AgentConnection Connection { get; }

    // A poll as the loop makes one: a number, the clock's time, the agent's version, the poll timeout of agent.json,
    // two bases with three free workers in all, and no received commands.
    public OutpostPollRequest Poll()
    {
        OutpostPollMode free = new(1, RunLimit);

        return new OutpostPollRequest(
            7,
            Clock.GetUtcNow(),
            PairedFolder.Version,
            FakeServer.PollTimeout,
            [new OutpostPollBase("accounting", free, free), new OutpostPollBase("warehouse", free, null)],
            []);
    }

    public Task<PollResult> PollAsync(OutpostPollRequest poll)
    {
        return Connection.PollAsync(poll, FakeServer.LeaseGrace, RequestTimeout, TestContext.Current.CancellationToken);
    }

    public Task<OutcomeResultEnum> SendOutcomeAsync(Guid command, byte[] outcome)
    {
        return Connection.SendOutcomeAsync(command, outcome, RequestTimeout, TestContext.Current.CancellationToken);
    }

    // Completes once the connection has armed a timer due at or before dueBy.
    public Task WaitForTimerAsync(DateTimeOffset dueBy)
    {
        return TestTimes.WaitForTimerAsync(Clock, dueBy);
    }

    // Waits until the connection has armed a timer due by the delay from now, states that none is due a millisecond
    // earlier, and moves the clock by the delay.
    public Task PassExactlyAsync(TimeSpan delay)
    {
        return TestTimes.PassExactlyAsync(Clock, delay, "connection");
    }

    public void Dispose()
    {
        Connection.Dispose();
        _pinnedKey.Dispose();
        _serverKey.Dispose();
        AgentKey.Dispose();
    }
}

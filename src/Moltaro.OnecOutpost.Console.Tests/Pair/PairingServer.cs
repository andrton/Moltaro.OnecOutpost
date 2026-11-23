using System.Security.Cryptography;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.TestSupport.FakeHttp;

namespace Moltaro.OnecOutpost.Console.Tests.Pair;

// The stub of the pair endpoint of §6.1 of docs/protocol.md that a test's machine reaches, which needs no store: the
// fake server, signing with a test key of its own, as the server's handler of ConsoleParts.Handlers, each request a line
// "request pair" of the machine's operation list; the pairing token that the application would give for its pending
// agent; and its answers.
internal sealed class PairingServer : IDisposable
{
    public static readonly Uri Address = new("https://outpost.example.com/agents/");

    // The pending agent that the token names.
    public static readonly Guid Agent = Guid.Parse("5f0c3a1e-2b4d-4c6e-8a10-b2c3d4e5f607");

    // The options of the server that its answer carries, not those of agent.json's example.
    public static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(40);

    public static readonly TimeSpan LeaseGrace = TimeSpan.FromSeconds(15);

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private readonly TestMachine _machine;

    public PairingServer(TestMachine machine)
    {
        _machine = machine;
        Secret = RandomNumberGenerator.GetBytes(16);
        Server = new FakeServer(_key, machine.Clock, machine.Data);
        Token = new OutpostPairingTokenText(Agent, Secret, OutpostKeys.Fingerprint(ServerKey)).Write();
        machine.Server = Server;
    }

    public FakeServer Server { get; }

    // The secret of the token, which never travels.
    public byte[] Secret { get; }

    public string Token { get; }

    // The 91 bytes of the server key, whose fingerprint the token holds.
    public byte[] ServerKey => _key.ExportSubjectPublicKeyInfo();

    // The pair request that the console sent, which comes within the deadline of the tests.
    public Task<ServerExchange> NextAsync()
    {
        return Server.NextAsync(OutpostRequestKindEnum.Pair);
    }

    // The request as the server reads it, checked against the token's identity and the request's own key.
    public static OutpostPairRequest Read(ServerExchange exchange)
    {
        return OutpostCodec.ReadPairRequest(Agent, exchange.Body, exchange.Signature);
    }

    // The body of the answer 200 that pairs the key sent, at the time of the machine's clock.
    public byte[] Answer(byte[] sentKey)
    {
        return Answer(sentKey, ServerKey);
    }

    // The same, naming another server key, as a hop that opens TLS answers with a key of its own.
    public byte[] Answer(byte[] sentKey, byte[] serverKey)
    {
        return OutpostCodec.Write(new OutpostPairAnswer(_machine.Clock.GetUtcNow(), Agent, sentKey, serverKey, PollTimeout, LeaseGrace));
    }

    public void Dispose()
    {
        Server.Dispose();
        _key.Dispose();
    }
}

using System.Net;
using System.Security.Cryptography;
using System.Text;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeHttp;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Pair;

// Anything but a verified 200 ends pair with PairingFailed and writes nothing (§6.1
// and §9 of docs/protocol.md), one case for each AgentPairingFailureEnum. The line gives "HTTP {status}: " when a status
// came and then the console's own reason, in the words of §6.1 for a server key that does not match the token, a
// signature that does not verify, the unsigned answer of a protocol version that the server does not speak, and a bare
// 403. It never shows text taken from a body it has not verified: each body of the stub that could be shown carries a
// mark, which the output never holds. The wait for the answer is the policy's requestTimeout, 20 seconds, on the fake
// clock.
public sealed class PairRefusalTests : IDisposable
{
    private const string Mark = "text-of-an-unverified-body";

    private readonly TestMachine _machine = new();

    private readonly PairingServer _server;

    public PairRefusalTests()
    {
        _machine.Installed();
        _server = new PairingServer(_machine);
    }

    public void Dispose()
    {
        _server.Dispose();
    }

    // An empty reason is one that §6.1 leaves to the console's own words.
    [Theory]
    [InlineData("a bare 403 with a page", 403, "a bare 403: the server answers so to any failure of pairing, and so can anything on the path")]
    [InlineData("the unsigned answer of a version not spoken", 400, "an unsigned answer in the form the server uses for a protocol version it does not speak")]
    [InlineData("an answer with another server key", 200, "the server's key does not match the fingerprint in the token")]
    [InlineData("an answer signed by another key", 200, "the answer's signature does not verify")]
    [InlineData("a signed 200 out of the form of the answer", 200, "")]
    [InlineData("a 500 with a page", 500, "")]
    public async Task An_answer_that_pair_does_not_accept_ends_it_with_PairingFailed_its_status_and_the_console_own_reason(
        string answer,
        int status,
        string reason)
    {
        Task<CommandAppResult> run = StartPair();
        ServerExchange exchange = await _server.NextAsync();
        Give(exchange, answer);
        CommandAppResult result = await TestTimes.EndOf(run);

        Assert.Equal(ConsoleExitCodes.PairingFailed, result.ExitCode);
        Assert.StartsWith($"PairingFailed (12): HTTP {status}: {reason}", LineOf(result));
        Assert.DoesNotContain(Mark, result.Output);
        AssertNothingWritten();
    }

    [Fact]
    public async Task A_pairing_whose_connection_fails_ends_with_PairingFailed_without_a_status()
    {
        Task<CommandAppResult> run = StartPair();
        (await _server.NextAsync()).Fail(new HttpRequestException("The connection was reset."));
        CommandAppResult result = await TestTimes.EndOf(run);

        Assert.Equal(ConsoleExitCodes.PairingFailed, result.ExitCode);
        Assert.DoesNotContain("HTTP", LineOf(result));
        AssertNothingWritten();
    }

    [Fact]
    public async Task A_pairing_without_an_answer_within_the_request_timeout_of_the_policy_ends_with_PairingFailed_without_a_status()
    {
        Task<CommandAppResult> run = StartPair();
        await _server.NextAsync();
        await TestTimes.PassExactlyAsync(_machine.Clock, TimeSpan.FromSeconds(20), "pairing");
        CommandAppResult result = await TestTimes.EndOf(run);

        Assert.Equal(ConsoleExitCodes.PairingFailed, result.ExitCode);
        Assert.DoesNotContain("HTTP", LineOf(result));
        AssertNothingWritten();
    }

    // The one line of the end, which starts with the kind and the code.
    private static string LineOf(CommandAppResult result)
    {
        return Assert.Single(TestMachine.LinesOf(result), line => line.StartsWith("PairingFailed (12): ", StringComparison.Ordinal));
    }

    private void AssertNothingWritten()
    {
        Assert.Equal(["others \"\"", "request pair"], _machine.Operations);
        Assert.False(_machine.Data.Root.Has("agent.json"));
        Assert.False(_machine.Data.Root.Has("secrets.dat"));
    }

    private Task<CommandAppResult> StartPair()
    {
        return _machine.Start(TestContext.Current.CancellationToken, "pair", PairingServer.Address.ToString(), _server.Token);
    }

    private void Give(ServerExchange exchange, string answer)
    {
        byte[] sentKey = PairingServer.Read(exchange).Key;

        switch (answer)
        {
            case "a bare 403 with a page":
                exchange.Answer(Page(HttpStatusCode.Forbidden));
                break;
            case "the unsigned answer of a version not spoken":
                exchange.AnswerUnsupportedVersion();
                break;
            case "an answer with another server key":
                // A hop that opens TLS answers with a key of its own, whose fingerprint is not the token's.
                using (ECDsa hop = ECDsa.Create(ECCurve.NamedCurves.nistP256))
                {
                    exchange.AnswerSignedBy(hop, 200, _server.Answer(sentKey, hop.ExportSubjectPublicKeyInfo()));
                }

                break;
            case "an answer signed by another key":
                using (ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256))
                {
                    exchange.AnswerSignedBy(other, 200, _server.Answer(sentKey));
                }

                break;
            case "a signed 200 out of the form of the answer":
                exchange.AnswerSigned(200, Encoding.UTF8.GetBytes($$"""{"error":"{{Mark}}"}"""));
                break;
            case "a 500 with a page":
                exchange.Answer(Page(HttpStatusCode.InternalServerError));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(answer), answer, "No such answer.");
        }
    }

    // A page of a hop on the path, without an Outpost-Signature.
    private static HttpResponseMessage Page(HttpStatusCode status)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent($"<html><body>{Mark}</body></html>", Encoding.UTF8, "text/html"),
        };
    }
}

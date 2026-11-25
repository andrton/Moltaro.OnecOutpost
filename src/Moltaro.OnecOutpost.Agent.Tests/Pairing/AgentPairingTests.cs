using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Moltaro.OnecOutpost.Agent.Pairing;
using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeHttp;

namespace Moltaro.OnecOutpost.Agent.Tests.Pairing;

// Pairing (§6.1 and §9 of docs/protocol.md, concept "Pairing"): the agent makes a P-256
// key, sends pair with the proof of that key under the token's secret, signed with that key, and reads the answer with
// ReadPairAnswer, which checks the server key against the token's fingerprint before it verifies the signature with
// it. A verified 200 gives the settings of agent.json and the key in PKCS#8; anything else ends the pairing with the
// status when one came and the failure: Unreachable without one, Bare for a bare 403, ProtocolVersion for the
// unsigned body of §9, ServerKey and Signature for the codec's codes, Answer for anything else. The agent waits
// requestTimeout over the answer and its body, read within 64 KiB. It writes no file: the console does.
public sealed class AgentPairingTests : IDisposable
{
    private static readonly Uri Address = new("https://outpost.example.com/agents/");

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private static readonly Guid Agent = Guid.Parse("3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b");

    private readonly FakeClock _clock = new(TestTimes.Start);

    private readonly ECDsa _serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private readonly byte[] _secret = RandomNumberGenerator.GetBytes(16);

    private readonly FakeServer _server;

    private readonly OutpostPairingTokenText _token;

    public AgentPairingTests()
    {
        _server = new FakeServer(_serverKey, _clock);
        _token = new OutpostPairingTokenText(Agent, _secret, OutpostKeys.Fingerprint(ServerKey));
    }

    private byte[] ServerKey => _serverKey.ExportSubjectPublicKeyInfo();

    public void Dispose()
    {
        _server.Dispose();
        _serverKey.Dispose();
    }

    [Fact]
    public async Task The_agent_sends_a_new_P256_key_with_its_proof_signed_by_that_key_and_a_verified_200_gives_the_settings_and_the_key()
    {
        Task<AgentPairingResult> pairing = PairAsync();
        ServerExchange exchange = await _server.NextAsync(OutpostRequestKindEnum.Pair);

        Assert.Equal(new Uri("https://outpost.example.com/agents/v1/pair"), exchange.Uri);
        Assert.Equal(HttpMethod.Post, exchange.Method);
        Assert.Equal(HttpVersion.Version11, exchange.Version);
        Assert.Equal("application/json", exchange.ContentType);
        Assert.Equal(Agent, exchange.Agent);
        Assert.Equal<string>(["identity"], exchange.Values("Accept-Encoding"));
        Assert.NotEqual(true, exchange.ExpectContinue);
        OutpostPairRequest request = OutpostCodec.ReadPairRequest(Agent, exchange.Body, exchange.Signature);
        Assert.Equal(OutpostCodec.Write(request), exchange.Body);
        Assert.Equal(PairedFolder.Version, request.AgentVersion);
        Assert.True(OutpostPairingProof.Check(_secret, Agent, request.Key, request.Proof));

        exchange.AnswerSigned(200, Answer(request.Key));
        AgentPairingResult result = await TestTimes.EndOf(pairing);

        Assert.Null(result.Failure);
        AgentPairingSettings settings = Assert.IsType<AgentPairingSettings>(result.Settings);
        Assert.Equal(Address, settings.Server);
        Assert.Equal(Agent, settings.Agent);
        Assert.Equal(ServerKey, settings.ServerKey);
        Assert.Equal(TimeSpan.FromSeconds(40), settings.PollTimeout);
        Assert.Equal(TimeSpan.FromSeconds(15), settings.LeaseGrace);

        using ECDsa key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(Assert.IsType<byte[]>(result.PrivateKey), out int read);
        Assert.Equal(result.PrivateKey!.Length, read);
        Assert.Equal(256, key.KeySize);
        Assert.Equal(request.Key, OutpostKeys.ExportPublicKey(key));
    }

    // "Bare" means without an Outpost-Signature that verifies, whatever the body (§9), so a 403 with a page of its own
    // is Bare too.
    [Theory]
    [InlineData("bare-403", 403, "Bare")]
    [InlineData("403-with-a-page", 403, "Bare")]
    [InlineData("protocol-version", 400, "ProtocolVersion")]
    [InlineData("another-server-key", 200, "ServerKey")]
    [InlineData("signed-by-another-key", 200, "Signature")]
    [InlineData("another-agent-key", 200, "Answer")]
    [InlineData("content-type-with-charset", 200, "Answer")]
    [InlineData("redirect", 302, "Answer")]
    [InlineData("bare-500", 500, "Answer")]
    public async Task Anything_but_a_verified_200_ends_the_pairing_with_its_status_and_failure(
        string answer,
        int status,
        string failure)
    {
        Task<AgentPairingResult> pairing = PairAsync();
        ServerExchange exchange = await _server.NextAsync(OutpostRequestKindEnum.Pair);
        byte[] sentKey = OutpostCodec.ReadPairRequest(Agent, exchange.Body, exchange.Signature).Key;

        Give(exchange, answer, sentKey);
        AgentPairingResult result = await TestTimes.EndOf(pairing);

        Assert.Null(result.Settings);
        Assert.Null(result.PrivateKey);
        Assert.Equal(status, result.Status);
        Assert.Equal(Enum.Parse<ServerAnswerCauseEnum>(failure), result.Failure);
    }

    [Fact]
    public async Task A_pairing_whose_connection_fails_ends_as_Unreachable_without_a_status()
    {
        Task<AgentPairingResult> pairing = PairAsync();
        (await _server.NextAsync(OutpostRequestKindEnum.Pair)).Fail(new HttpRequestException("The connection was reset."));
        AgentPairingResult result = await TestTimes.EndOf(pairing);

        AssertEnded(result, null, ServerAnswerCauseEnum.Unreachable);
    }

    [Fact]
    public async Task A_pairing_that_gets_no_answer_within_the_request_timeout_ends_as_Unreachable()
    {
        DateTimeOffset start = _clock.GetUtcNow();

        Task<AgentPairingResult> pairing = PairAsync();
        await _server.NextAsync(OutpostRequestKindEnum.Pair);
        await TestTimes.WaitForTimerAsync(_clock, start + RequestTimeout);
        Assert.False(_clock.HasTimerDueBy(start + RequestTimeout - TestTimes.Millisecond));
        _clock.Advance(RequestTimeout);
        AgentPairingResult result = await TestTimes.EndOf(pairing);

        AssertEnded(result, null, ServerAnswerCauseEnum.Unreachable);
    }

    // §10: each limit is accepted at it and refused one byte beyond it. White space after the answer keeps §2.
    [Fact]
    public async Task A_pairing_answer_of_64_KiB_is_read()
    {
        Task<AgentPairingResult> pairing = PairAsync();
        ServerExchange exchange = await _server.NextAsync(OutpostRequestKindEnum.Pair);
        byte[] written = Answer(OutpostCodec.ReadPairRequest(Agent, exchange.Body, exchange.Signature).Key);
        byte[] body = new byte[OutpostProtocol.AnswerLimit];
        written.CopyTo(body, 0);
        body.AsSpan(written.Length).Fill((byte)' ');

        exchange.AnswerSigned(200, body);
        AgentPairingResult result = await TestTimes.EndOf(pairing);

        Assert.NotNull(result.Settings);
        Assert.Null(result.Failure);
    }

    // Its failure, as for every answer whose status came and whose body did not, is stated by the test after this one.
    [Fact]
    public async Task A_pairing_answer_declared_beyond_64_KiB_ends_the_pairing_unread()
    {
        GeneratedStream body = new(OutpostProtocol.AnswerLimit + 1L);

        Task<AgentPairingResult> pairing = PairAsync();
        ServerExchange exchange = await _server.NextAsync(OutpostRequestKindEnum.Pair);
        StreamContent content = new(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.ContentLength = body.Length;
        exchange.Answer(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        AgentPairingResult result = await TestTimes.EndOf(pairing);

        Assert.Null(result.Settings);
        Assert.Null(result.PrivateKey);
        Assert.Equal(200, result.Status);
        Assert.NotNull(result.Failure);
        Assert.Equal(0, body.BytesRead);
    }

    // PairAsync preserves the HTTP status when it receives one. It reports Unreachable without a status and Answer
    // for an otherwise unrecognized reply. A 200 whose body is beyond 64 KiB, or does not come within requestTimeout,
    // is an answer whose status came.
    [Theory]
    [InlineData("beyond-64-KiB")]
    [InlineData("late")]
    public async Task A_pairing_answer_whose_status_came_and_whose_body_did_not_ends_with_that_status_and_Answer(string body)
    {
        DateTimeOffset start = _clock.GetUtcNow();

        Task<AgentPairingResult> pairing = PairAsync();
        ServerExchange exchange = await _server.NextAsync(OutpostRequestKindEnum.Pair);

        if (body == "late")
        {
            // The one wait covers the answer and its body, so its timer is armed before the answer begins: the clock moves
            // only once the agent has the status and waits for the rest of the body.
            byte[] sentKey = OutpostCodec.ReadPairRequest(Agent, exchange.Body, exchange.Signature).Key;
            StalledStream stalled = exchange.AnswerStalled(Answer(sentKey));
            await TestTimes.WaitForTimerAsync(_clock, start + RequestTimeout);
            await stalled.Stalled.WaitAsync(TestTimes.Deadline, TestContext.Current.CancellationToken);
            _clock.Advance(RequestTimeout);
        }
        else
        {
            exchange.AnswerStreamed(new GeneratedStream(OutpostProtocol.AnswerLimit + 1L), declared: true);
        }

        AssertEnded(await TestTimes.EndOf(pairing), 200, ServerAnswerCauseEnum.Answer);
    }

    private static void AssertEnded(AgentPairingResult result, int? status, ServerAnswerCauseEnum failure)
    {
        Assert.Null(result.Settings);
        Assert.Null(result.PrivateKey);
        Assert.Equal(status, result.Status);
        Assert.Equal(failure, result.Failure);
    }

    private Task<AgentPairingResult> PairAsync()
    {
        return AgentPairing.PairAsync(
            Address,
            _token,
            PairedFolder.Version,
            _server,
            RequestTimeout,
            _clock,
            TestContext.Current.CancellationToken);
    }

    // The answer 200 of §6.1 for the key sent, with options that are not those of agent.json's example.
    private byte[] Answer(byte[] sentKey)
    {
        return OutpostCodec.Write(new OutpostPairAnswer(
            _clock.GetUtcNow(),
            Agent,
            sentKey,
            ServerKey,
            TimeSpan.FromSeconds(40),
            TimeSpan.FromSeconds(15)));
    }

    private void Give(ServerExchange exchange, string answer, byte[] sentKey)
    {
        switch (answer)
        {
            case "bare-403":
                exchange.AnswerBare(403);
                break;
            case "403-with-a-page":
                exchange.Answer(new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("<html><body>Forbidden</body></html>", System.Text.Encoding.UTF8, "text/html"),
                });
                break;
            case "protocol-version":
                exchange.AnswerUnsupportedVersion();
                break;
            case "another-server-key":
                // A hop that opens TLS answers with a key of its own, whose fingerprint is not the token's.
                using (ECDsa hop = ECDsa.Create(ECCurve.NamedCurves.nistP256))
                {
                    byte[] body = OutpostCodec.Write(new OutpostPairAnswer(
                        _clock.GetUtcNow(),
                        Agent,
                        sentKey,
                        hop.ExportSubjectPublicKeyInfo(),
                        TimeSpan.FromSeconds(40),
                        TimeSpan.FromSeconds(15)));

                    exchange.AnswerSignedBy(hop, 200, body);
                }

                break;
            case "signed-by-another-key":
                using (ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256))
                {
                    exchange.AnswerSignedBy(other, 200, Answer(sentKey));
                }

                break;
            case "another-agent-key":
                using (ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP256))
                {
                    exchange.AnswerSigned(200, Answer(other.ExportSubjectPublicKeyInfo()));
                }

                break;
            case "content-type-with-charset":
                exchange.AnswerSigned(200, Answer(sentKey), "application/json; charset=utf-8");
                break;
            case "redirect":
                exchange.AnswerRedirect();
                break;
            case "bare-500":
                exchange.AnswerBare(500);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(answer), answer, "No such answer.");
        }
    }
}

using System.Net.Http.Headers;
using System.Security.Cryptography;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.Server;
using Xunit;

namespace Moltaro.OnecOutpost.TestSupport.Loopback;

// An agent of the protocol, made of the internal types of the protocol assembly, that speaks to a LoopbackServer over
// HTTP as docs/protocol.md says an agent does: it pairs with its pairing token and a key of its own, pins the server
// key from the verified answer (§6.1), signs every request (§3), verifies every answer that it reads (§9) and every
// command that an answer hands it (§7), and sends outcomes (§6.3). A test drives it request by request and reads what
// came; it decides nothing that the agent program decides, such as when to poll again, which commands its next poll
// lists as received, or what to do with a lost answer.
internal sealed class TestAgent : IDisposable
{
    private const string ContentType = "application/json";

    private readonly HttpClient _client;

    private readonly TimeProvider _clock;

    private readonly OutpostPairingTokenText _token;

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    // The server key, pinned from a verified answer to pair; null before.
    private ECDsa? _serverKey;

    public TestAgent(HttpClient client, TimeProvider clock, OutpostPairingToken token)
    {
        Assert.True(
            OutpostPairingTokenText.TryRead(token.Token, out OutpostPairingTokenText? text),
            "The token is not in the form of §5.");

        _client = client;
        _clock = clock;
        _token = text;
        PublicKey = OutpostKeys.ExportPublicKey(_key);
        Proof = OutpostPairingProof.Compute(_token.Secret, _token.Agent, PublicKey);
    }

    // The identity that the token names, which the agent has once it is paired.
    public Guid Id => _token.Agent;

    // Its public key in the 91-byte form of §2.
    public byte[] PublicKey { get; }

    // The pairing proof of its key with the secret of the token (§3).
    public byte[] Proof { get; }

    // The number of its last poll; 0 before its first.
    public long Number { get; private set; }

    public void Dispose()
    {
        _key.Dispose();
        _serverKey?.Dispose();
    }

    // The body of its pair request: its version, its key and the proof (§6.1).
    public byte[] PairBody()
    {
        return OutpostCodec.Write(new OutpostPairRequest(PollRequests.AgentVersion, PublicKey, Proof));
    }

    public Task<AgentAnswer> SendPairAsync(CancellationToken cancellationToken)
    {
        return SendAsync(OutpostRequestKindEnum.Pair, PairBody(), cancellationToken);
    }

    // Sends pair and pins the server key from the answer, which has to be a verified 200.
    public async Task<OutpostPairAnswer> PairAsync(CancellationToken cancellationToken)
    {
        AgentAnswer answer = await SendPairAsync(cancellationToken);

        return Pin(answer);
    }

    // Checks an answer to pair as §6.1 says the agent checks it, and pins the server key: a 200 whose server key has
    // the fingerprint of the token, whose signature verifies with that key for this request, and that names this agent
    // and the key it sent.
    public OutpostPairAnswer Pin(AgentAnswer answer)
    {
        Assert.Equal(200, answer.Status);
        Assert.Equal(ContentType, answer.ContentType);

        OutpostPairAnswer pairAnswer = OutpostCodec.ReadPairAnswer(
            _token.Fingerprint,
            Id,
            PublicKey,
            answer.RequestBody,
            answer.Body,
            answer.SignatureBytes);

        _serverKey?.Dispose();
        _serverKey = OutpostKeys.ImportPublicKey(pairAnswer.ServerKey);

        return pairAnswer;
    }

    // Its next poll: the next number, the time of the clock, the version of the tests, the poll timeout given and the
    // bases declared, without received commands.
    public OutpostPollRequest NextPoll(TimeSpan pollTimeout, IReadOnlyList<OutpostPollBase> bases)
    {
        return NextPoll(pollTimeout, bases, []);
    }

    // Its next poll, which lists the commands given as received (§6.2).
    public OutpostPollRequest NextPoll(
        TimeSpan pollTimeout,
        IReadOnlyList<OutpostPollBase> bases,
        IReadOnlyList<Guid> received)
    {
        Number++;

        return PollRequests.Create(Number, _clock.GetUtcNow(), pollTimeout, bases) with { Received = received };
    }

    public Task<AgentAnswer> PollAsync(OutpostPollRequest poll, CancellationToken cancellationToken)
    {
        return SendAsync(OutpostRequestKindEnum.Poll, OutpostCodec.Write(poll), cancellationToken);
    }

    // Checks that an answer to the poll is a verified 200 (§9) and reads it (§6.2).
    public OutpostPollAnswer ReadPollAnswer(OutpostPollRequest poll, AgentAnswer answer)
    {
        Assert.Equal(200, answer.Status);
        Assert.Equal(ContentType, answer.ContentType);

        int free = poll.Bases.Sum(pollBase => pollBase.Read.Free + (pollBase.Write?.Free ?? 0));

        return OutpostCodec.ReadPollAnswer(
            PinnedKey,
            Id,
            free,
            answer.RequestBody,
            answer.Body,
            answer.SignatureBytes);
    }

    // The commands that a verified poll answer hands out, in its order, each verified with the pinned server key and
    // read as §7 says.
    public IReadOnlyList<OutpostCommandMessage> ReadCommands(OutpostPollAnswer answer)
    {
        return [.. answer.Commands.Select(command => OutpostCodec.ReadCommand(PinnedKey, command))];
    }

    // Sends an outcome request for the command, with the outcome object written as the agent writes the outcomes of its
    // own (§8).
    public Task<AgentAnswer> SendOutcomeAsync(
        Guid commandId,
        OutpostOutcomeMessage outcome,
        CancellationToken cancellationToken)
    {
        return SendOutcomeAsync(commandId, OutpostCodec.Write(outcome), cancellationToken);
    }

    // Sends an outcome request for the command with the exact bytes of an outcome object, as the agent forwards the
    // object of the executor (§8).
    public Task<AgentAnswer> SendOutcomeAsync(Guid commandId, byte[] outcome, CancellationToken cancellationToken)
    {
        byte[] body = OutpostCodec.Write(new OutpostOutcomeRequest(commandId, outcome));

        return SendAsync(OutpostRequestKindEnum.Outcome, body, cancellationToken);
    }

    // Checks that an answer to an outcome request is a verified 200 that confirms the command (§6.3, §9), and reads it.
    public OutpostOutcomeAnswer ReadOutcomeAnswer(Guid commandId, AgentAnswer answer)
    {
        Assert.Equal(200, answer.Status);
        Assert.Equal(ContentType, answer.ContentType);

        return OutpostCodec.ReadOutcomeAnswer(
            PinnedKey,
            Id,
            commandId,
            answer.RequestBody,
            answer.Body,
            answer.SignatureBytes);
    }

    // Sends the outcome of the command and checks that the server confirmed it.
    public async Task<OutpostOutcomeAnswer> ConfirmOutcomeAsync(
        Guid commandId,
        OutpostOutcomeMessage outcome,
        CancellationToken cancellationToken)
    {
        AgentAnswer answer = await SendOutcomeAsync(commandId, outcome, cancellationToken);

        return ReadOutcomeAnswer(commandId, answer);
    }

    // Checks that an answer is a verified signed refusal of §9, bound to the request as the server received it, and
    // reads it.
    public OutpostRefusal ReadRefusal(AgentAnswer answer)
    {
        Assert.Equal(ContentType, answer.ContentType);

        return OutpostCodec.ReadRefusal(
            PinnedKey,
            answer.Kind,
            Id,
            answer.Status,
            answer.RequestBody,
            answer.Body,
            answer.SignatureBytes);
    }

    // The signature of a request of this agent over the body (§3).
    public byte[] Sign(OutpostRequestKindEnum kind, byte[] body)
    {
        return SignAs(Id, kind, body);
    }

    // A signature with this agent's key over a request that names another agent: what the other agent's request gets
    // when it is signed with the key of this one.
    public byte[] SignAs(Guid agent, OutpostRequestKindEnum kind, byte[] body)
    {
        return OutpostSignatures.SignRequest(_key, kind, agent, body);
    }

    // The header lines of §4 for a request of this agent, Content-Type, Outpost-Agent and Outpost-Signature, as a test
    // writes them by hand.
    public List<(string Name, string Value)> Headers(byte[] signature)
    {
        return
        [
            ("Content-Type", ContentType),
            (OutpostProtocol.AgentHeader, OutpostHeaders.FormatAgent(Id)),
            (OutpostProtocol.SignatureHeader, OutpostHeaders.FormatSignature(signature)),
        ];
    }

    public Task<AgentAnswer> SendAsync(OutpostRequestKindEnum kind, byte[] body, CancellationToken cancellationToken)
    {
        return SendAsync(kind, body, Sign(kind, body), cancellationToken);
    }

    // Sends the request of an earlier answer again, its exact bytes with its signature: an exact replay.
    public Task<AgentAnswer> ReplayAsync(AgentAnswer earlier, CancellationToken cancellationToken)
    {
        return SendAsync(earlier.Kind, earlier.RequestBody, earlier.RequestSignature, cancellationToken);
    }

    // Sends the exact bytes of a request to v1 with the headers of §4: Content-Type, Outpost-Agent with this agent's
    // identity and Outpost-Signature with the signature given. An outcome carries Expect: 100-continue, as the agent
    // sends it (§4).
    public async Task<AgentAnswer> SendAsync(
        OutpostRequestKindEnum kind,
        byte[] body,
        byte[] signature,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "v1/" + OutpostForms.WriteRequestKind(kind));
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(ContentType);
        request.Headers.Add(OutpostProtocol.AgentHeader, OutpostHeaders.FormatAgent(Id));
        request.Headers.Add(OutpostProtocol.SignatureHeader, OutpostHeaders.FormatSignature(signature));
        request.Headers.ExpectContinue = kind == OutpostRequestKindEnum.Outcome;

        using HttpResponseMessage response = await _client.SendAsync(request, cancellationToken);

        string? answerSignature = null;

        if (response.Headers.TryGetValues(OutpostProtocol.SignatureHeader, out IEnumerable<string>? values))
        {
            answerSignature = values.Single();
        }

        byte[] answerBody = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        return new AgentAnswer(
            kind,
            (int)response.StatusCode,
            response.Content.Headers.ContentType?.ToString(),
            answerSignature,
            answerBody,
            body,
            signature);
    }

    private ECDsa PinnedKey => _serverKey ?? throw new InvalidOperationException("The agent pinned no server key.");
}

using System.Security.Cryptography;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Server.Store;

namespace Moltaro.OnecOutpost.Server.Tests.Store;

// A paired agent of the store, for the tests whose subject comes after pairing. It is created with a pairing token that
// is redeemed at once, as the endpoint of pair does once the proof checks. Its polls are recorded as the endpoint of
// poll records them, numbered upwards from 1, at the time of the clock, with the defaults of the options: a poll
// timeout of 25 seconds in the poll and in the option, so a hold of 25 seconds, a lease grace of 10 seconds and a
// clock skew of 2 minutes. The tests of commands give their polls a list of received commands and, where the agent's
// clock is their subject, a signed time of its own; they take commands with a poll and send outcomes as the endpoints
// of poll and outcome do.
internal sealed class StoreAgent
{
    public static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(25);

    // The hold is the smaller of the poll timeout of the poll and of the option (§6.2 of docs/protocol.md).
    public static readonly TimeSpan Hold = PollTimeout;

    public static readonly TimeSpan LeaseGrace = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

    private readonly OutpostStore _store;

    private readonly TimeProvider _clock;

    private StoreAgent(OutpostStore store, TimeProvider clock, Guid id)
    {
        _store = store;
        _clock = clock;
        Id = id;
    }

    public Guid Id { get; }

    // The number of its last poll; 0 before its first.
    public long Number { get; private set; }

    // The store keeps a key as it is given, so random bytes stand in for the secret and for the agent's key.
    public static async Task<StoreAgent> PairAsync(
        OutpostStore store,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        Guid id = Guid.NewGuid();
        byte[] secret = RandomNumberGenerator.GetBytes(16);
        DateTimeOffset expiresAt = clock.GetUtcNow() + TimeSpan.FromMinutes(15);

        await store.CreatePairingTokenAsync(id, "Test agent", secret, expiresAt, cancellationToken);
        Assert.True(await store.RedeemPairingTokenAsync(id, RandomNumberGenerator.GetBytes(91), cancellationToken));

        return new StoreAgent(store, clock, id);
    }

    // Records the agent's next poll, which declares the bases given.
    public async Task<PollState> PollAsync(IReadOnlyList<OutpostPollBase> bases, CancellationToken cancellationToken)
    {
        Number++;

        OutpostPollRequest poll = PollRequests.Create(Number, _clock.GetUtcNow(), PollTimeout, bases);

        return (await _store.RecordPollAsync(Id, poll, Hold, LeaseGrace, ClockSkew, cancellationToken)).State;
    }

    // The agent's next poll, numbered after its last, which declares the bases given and lists the commands given as
    // received. It is signed at the time given, or at the time of the clock.
    public OutpostPollRequest NextPoll(
        IReadOnlyList<OutpostPollBase> bases,
        IReadOnlyList<Guid> received,
        DateTimeOffset? time = null)
    {
        Number++;

        return new OutpostPollRequest(
            Number,
            time ?? _clock.GetUtcNow(),
            PollRequests.AgentVersion,
            PollTimeout,
            bases,
            received);
    }

    // Records a poll of the agent, which need not be its next, as the endpoint of poll records it.
    public Task<PollRecord> RecordAsync(OutpostPollRequest poll, CancellationToken cancellationToken)
    {
        return _store.RecordPollAsync(Id, poll, Hold, LeaseGrace, ClockSkew, cancellationToken);
    }

    // Records the agent's next poll at the time of the clock, then takes commands with it once, with room for an
    // answer of the largest size (§10 of docs/protocol.md).
    public async Task<PollTake> PollAndTakeAsync(
        IReadOnlyList<OutpostPollBase> bases,
        IReadOnlyList<Guid> received,
        CancellationToken cancellationToken)
    {
        OutpostPollRequest poll = NextPoll(bases, received);

        await RecordAsync(poll, cancellationToken);

        return await _store.TakeCommandsAsync(Id, poll, StoreCommands.Room, cancellationToken);
    }

    // Sends the outcome object of a command, with its bytes as the codec writes them, as the endpoint of outcome
    // hands it to the store; true when the store kept it.
    public Task<bool> SendOutcomeAsync(
        Guid commandId,
        OutpostOutcomeMessage outcome,
        CancellationToken cancellationToken)
    {
        return _store.KeepOutcomeAsync(Id, commandId, outcome, OutpostCodec.Write(outcome), cancellationToken);
    }
}

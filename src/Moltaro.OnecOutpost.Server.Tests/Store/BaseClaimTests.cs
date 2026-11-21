using System.Security.Cryptography;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Server.Tests.PollRequests;

namespace Moltaro.OnecOutpost.Server.Tests.Store;

// The base keys of the store against a real PostgreSQL (concept rule 14): a key belongs to the first agent that
// declares it, until that agent is revoked or the key is released, and a release is refused while the newest poll of
// the holder declares the key, whatever its lease. Of the keys the newest poll of an agent declared, the state of its
// poll holds those whose claim names the agent and claims every other, both in ordinal order of the keys. Each test
// works in a schema of its own.
[Trait("Requires", "PostgreSQL")]
public sealed class BaseClaimTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task A_key_belongs_to_the_first_agent_that_declares_it_and_is_claimed_for_another_that_declares_it()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "key_claimed_first", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent first = await StoreAgent.PairAsync(store, clock, cancellationToken);
        StoreAgent second = await StoreAgent.PairAsync(store, clock, cancellationToken);

        PollState firstState = await first.PollAsync([ReadOnly("accounting")], cancellationToken);
        PollState secondState = await second.PollAsync([ReadOnly("accounting"), ReadOnly("sales")], cancellationToken);
        PollState firstAgain = await first.PollAsync([ReadOnly("accounting")], cancellationToken);

        Assert.Equal<string>(["accounting"], firstState.Held);
        Assert.Empty(firstState.Claimed);
        Assert.Equal<string>(["sales"], secondState.Held);
        Assert.Equal<string>(["accounting"], secondState.Claimed);
        Assert.Equal<string>(["accounting"], firstAgain.Held);
        Assert.Equal(first.Id, await schema.FindHolderAsync("accounting", cancellationToken));
    }

    // Under the collation of the test database, en_US.utf8, which weighs letters before punctuation, these keys sort
    // otherwise: "a.b" and "ab" before "a-c", and "ba" before "b-c".
    [Fact]
    public async Task The_lists_of_the_state_of_a_poll_are_in_ordinal_order_of_their_keys()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "keys_in_ordinal_order", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent holder = await StoreAgent.PairAsync(store, clock, cancellationToken);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await holder.PollAsync([ReadOnly("ba"), ReadOnly("b-c")], cancellationToken);

        PollState state = await agent.PollAsync(
            [ReadOnly("warehouse"), ReadOnly("ba"), ReadOnly("ab"), ReadOnly("b-c"), ReadOnly("a-c"), ReadOnly("a.b")],
            cancellationToken);

        Assert.Equal<string>(["a-c", "a.b", "ab", "warehouse"], state.Held);
        Assert.Equal<string>(["b-c", "ba"], state.Claimed);
    }

    [Fact]
    public async Task A_key_that_its_holder_no_longer_declares_stays_its_own_and_is_in_neither_of_its_lists()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "key_no_longer_declared", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent holder = await StoreAgent.PairAsync(store, clock, cancellationToken);
        StoreAgent other = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await holder.PollAsync([ReadOnly("accounting"), ReadOnly("warehouse")], cancellationToken);

        PollState holderState = await holder.PollAsync([ReadOnly("warehouse")], cancellationToken);
        PollState otherState = await other.PollAsync([ReadOnly("accounting")], cancellationToken);

        Assert.Equal<string>(["warehouse"], holderState.Held);
        Assert.Empty(holderState.Claimed);
        Assert.Equal(holder.Id, await schema.FindHolderAsync("accounting", cancellationToken));
        Assert.Empty(otherState.Held);
        Assert.Equal<string>(["accounting"], otherState.Claimed);
    }

    [Fact]
    public async Task A_release_of_a_key_that_no_agent_holds_gives_NotHeld()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "release_not_held", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await agent.PollAsync([ReadOnly("accounting")], cancellationToken);

        Assert.Equal(OutpostReleaseResultEnum.NotHeld, await store.ReleaseBaseAsync("warehouse", cancellationToken));
    }

    // The lease of the holder's newest poll ran out long ago: "still declares" means the newest poll, whatever its
    // lease, so that a holder down for maintenance finds its keys its own when it comes back.
    [Fact]
    public async Task A_release_while_the_newest_poll_of_the_holder_declares_the_key_gives_StillDeclared_whatever_its_lease()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "release_still_declared", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent holder = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await holder.PollAsync([ReadOnly("accounting")], cancellationToken);
        clock.Advance(TimeSpan.FromDays(1));

        OutpostReleaseResultEnum released = await store.ReleaseBaseAsync("accounting", cancellationToken);

        Assert.Equal(OutpostReleaseResultEnum.StillDeclared, released);
        Assert.Equal(holder.Id, await schema.FindHolderAsync("accounting", cancellationToken));
    }

    [Fact]
    public async Task A_release_of_a_key_its_holder_no_longer_declares_frees_it_for_the_next_agent_that_declares_it()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "release_released", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent holder = await StoreAgent.PairAsync(store, clock, cancellationToken);
        StoreAgent next = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await holder.PollAsync([ReadOnly("accounting")], cancellationToken);
        await holder.PollAsync([], cancellationToken);

        Assert.Equal(OutpostReleaseResultEnum.Released, await store.ReleaseBaseAsync("accounting", cancellationToken));

        Assert.Null(await schema.FindHolderAsync("accounting", cancellationToken));
        Assert.Equal(OutpostReleaseResultEnum.NotHeld, await store.ReleaseBaseAsync("accounting", cancellationToken));

        PollState state = await next.PollAsync([ReadOnly("accounting")], cancellationToken);

        Assert.Equal<string>(["accounting"], state.Held);
        Assert.Equal(next.Id, await schema.FindHolderAsync("accounting", cancellationToken));
    }

    // The key is freed while the poll of the second agent is held, as a release or a revocation frees it: the answer to
    // that poll reads the key as claimed, and claims nothing itself, until the agent's next poll takes the key.
    [Fact]
    public async Task A_declared_key_that_no_agent_holds_is_claimed_in_the_state_of_a_poll_until_the_next_poll_holds_it()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "freed_key_claimed", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent holder = await StoreAgent.PairAsync(store, clock, cancellationToken);
        StoreAgent agent = await StoreAgent.PairAsync(store, clock, cancellationToken);
        await holder.PollAsync([ReadOnly("accounting")], cancellationToken);
        await agent.PollAsync([ReadOnly("accounting")], cancellationToken);
        await holder.PollAsync([], cancellationToken);
        Assert.Equal(OutpostReleaseResultEnum.Released, await store.ReleaseBaseAsync("accounting", cancellationToken));

        PollState held = await store.ReadPollStateAsync(agent.Id, agent.Number, cancellationToken);

        Assert.Empty(held.Held);
        Assert.Equal<string>(["accounting"], held.Claimed);
        Assert.Null(await schema.FindHolderAsync("accounting", cancellationToken));

        PollState next = await agent.PollAsync([ReadOnly("accounting")], cancellationToken);

        Assert.Equal<string>(["accounting"], next.Held);
        Assert.Empty(next.Claimed);
    }

    // The race of two claims of one key, driven to a known order as the race of two redemptions of S3 is: another
    // connection holds an uncommitted claim of the key, for an agent of its own, until both polls are seen waiting for
    // it, and then rolls it back. Which poll takes the key is not the subject; that one holds it and the other finds it
    // claimed, changing nothing of the claim, is.
    [Fact]
    public async Task Of_two_polls_that_claim_one_key_at_once_one_holds_it_and_the_other_finds_it_claimed()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "key_claimed_at_once", cancellationToken);
        FakeClock clock = new();
        OutpostStore store = new(schema.ConnectionString, schema.Name, clock);
        StoreAgent first = await StoreAgent.PairAsync(store, clock, cancellationToken);
        StoreAgent second = await StoreAgent.PairAsync(store, clock, cancellationToken);
        Guid lockHolder = Guid.NewGuid();
        byte[] secret = RandomNumberGenerator.GetBytes(16);
        DateTimeOffset expiresAt = clock.GetUtcNow() + TimeSpan.FromMinutes(15);
        await store.CreatePairingTokenAsync(lockHolder, "Lock holder", secret, expiresAt, cancellationToken);

        await using HeldLock claimLock = await HeldLock.TakeAsync(
            schema.ConnectionString,
            $"INSERT INTO {schema.Name}.base_claims (key, agent_id) VALUES ('accounting', '{lockHolder}')",
            cancellationToken);

        Task<PollState> firstPoll = first.PollAsync([ReadOnly("accounting")], cancellationToken);
        Task<PollState> secondPoll = second.PollAsync([ReadOnly("accounting")], cancellationToken);

        await claimLock.WaitForAsync([firstPoll, secondPoll], cancellationToken);
        await claimLock.ReleaseAsync(cancellationToken);

        PollState firstState = await firstPoll;
        PollState secondState = await secondPoll;

        bool firstWon = firstState.Held.Count == 1;
        PollState won = firstWon ? firstState : secondState;
        PollState lost = firstWon ? secondState : firstState;

        Assert.Equal<string>(["accounting"], won.Held);
        Assert.Empty(won.Claimed);
        Assert.Empty(lost.Held);
        Assert.Equal<string>(["accounting"], lost.Claimed);
        Assert.Equal(firstWon ? first.Id : second.Id, await schema.FindHolderAsync("accounting", cancellationToken));
    }
}

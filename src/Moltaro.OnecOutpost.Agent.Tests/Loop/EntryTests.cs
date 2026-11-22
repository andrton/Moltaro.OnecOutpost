using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Tests.FakeHttp;
using Moltaro.OnecOutpost.Agent.Tests.Host;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Agent.Tests.AuditLines;

namespace Moltaro.OnecOutpost.Agent.Tests.Loop;

// The entries that the answers to polls make (§9 of docs/protocol.md, "The audit log" of the plan, the S6 brief,
// "AgentLoop.RunAsync"): connection once per change of the state of the polls, which the agent does not know when it
// starts, with the reason of §9 when lost; bases when the server's answer changes what the agent holds; and retention,
// run only right after a verified poll answer of the run whose time is within a day of the agent's clock.
public sealed class EntryTests
{
    // Each kind of reason of §9: the code of a verified refusal, protocol-version, status- and the status, unreachable.
    // A new reason while the state stays lost is no change.
    [Fact]
    public async Task Connection_is_recorded_once_per_change_of_the_state_of_the_polls_with_each_kind_of_reason()
    {
        PairedFolder folder = PairedFolder.Create();
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));
        TimeSpan tenth = TimeSpan.FromSeconds(3);

        (await host.NextPollAsync()).AnswerPoll(host.Server.Ordinary());
        (await host.NextPollAsync()).Refuse(400, OutpostProtocolCodes.Form);
        await host.PassAsync(tenth);
        (await host.NextPollAsync()).AnswerUnsupportedVersion();
        await host.PassAsync(2 * tenth);
        (await host.NextPollAsync()).AnswerPoll(host.Server.Ordinary());
        (await host.NextPollAsync()).AnswerBare(403);
        await host.PassAsync(tenth);
        (await host.NextPollAsync()).AnswerPoll(host.Server.Ordinary());
        (await host.NextPollAsync()).Fail(new HttpRequestException("The connection was reset."));
        await host.PassAsync(tenth);
        (await host.NextPollAsync()).AnswerPoll(host.Server.Ordinary());
        (await host.NextPollAsync()).AnswerUnsupportedVersion();
        await host.PassAsync(tenth);
        await host.NextPollAsync();

        List<string?> states = [.. Connections(host).Select(entry => Reason(entry) is string reason ? "lost " + reason : "connected")];

        Assert.Equal<string?>(
            ["connected", "lost form", "connected", "lost status-403", "connected", "lost unreachable", "connected", "lost protocol-version"],
            states);
        Assert.All(Connections(host).Where(entry => Reason(entry) is null), entry => Assert.Equal("connected", entry.GetProperty("state").GetString()));
        Assert.All(Connections(host).Where(entry => Reason(entry) is not null), entry => Assert.Equal("lost", entry.GetProperty("state").GetString()));
    }

    // Only the answers to polls count, so that an outcome held up on its path cannot make the state flap (§9).
    [Fact]
    public async Task An_outcome_whose_answers_keep_being_lost_while_the_polls_go_through_records_no_connection_entry()
    {
        PairedFolder folder = PairedFolder.Create();
        Guid command = EarlierRun.Ended(folder, OutpostModeEnum.Read, TestOutcomes.Succeeded());
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));

        (await host.NextPollAsync()).AnswerPoll(host.Server.Ordinary());
        await host.NextPollAsync();
        (await host.NextOutcomeAsync()).AnswerBare(503);
        await host.PassAsync(TimeSpan.FromSeconds(3));
        (await host.NextOutcomeAsync()).AnswerBare(502);
        await host.PassAsync(TimeSpan.FromSeconds(6));
        (await host.NextOutcomeAsync()).Confirm();
        await host.Data.WaitForAsync(change => change.Line == $"delete {Operations.OutboxFile(command)}");

        JsonElement connected = Assert.Single(Connections(host));
        Assert.Equal("connected", connected.GetProperty("state").GetString());
    }

    // The first answer of the run records what the agent holds; an answer that holds the same records nothing, one that
    // changes it records it again, and a lost answer records nothing.
    [Fact]
    public async Task Bases_is_recorded_when_an_answer_changes_what_the_agent_holds()
    {
        PairedFolder folder = PairedFolder.Create();
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));

        (await host.NextPollAsync()).AnswerPoll(host.Server.Ordinary() with { Held = ["accounting"], Claimed = ["warehouse"] });
        (await host.NextPollAsync()).AnswerPoll(host.Server.Ordinary() with { Held = ["accounting"], Claimed = ["warehouse"] });
        (await host.NextPollAsync()).AnswerPoll(host.Server.Ordinary() with { Held = ["accounting", "warehouse"], Claimed = [] });
        (await host.NextPollAsync()).AnswerBare(503);
        await host.PassAsync(TimeSpan.FromSeconds(3));
        await host.NextPollAsync();

        List<JsonElement> bases = [.. AuditEntries.AppendedSince(host.Data, 0).Where(entry => AuditEntries.Kind(entry) == "bases")];

        Assert.Equal(2, bases.Count);
        Assert.Equal<string?>(["accounting"], AuditEntries.Strings(bases[0].GetProperty("held")));
        Assert.Equal<string?>(["warehouse"], AuditEntries.Strings(bases[0].GetProperty("claimed")));
        Assert.Equal<string?>(["accounting", "warehouse"], AuditEntries.Strings(bases[1].GetProperty("held")));
        Assert.Empty(AuditEntries.Strings(bases[1].GetProperty("claimed")));
    }

    // A file whose last entry is 100 days old, past auditRetentionDays plus a day. Nothing removes it at the start, after
    // a lost answer, or after an answer whose time is two days ahead of the agent's clock; the answer whose time is the
    // clock's removes it.
    [Fact]
    public async Task Retention_runs_only_after_a_verified_poll_answer_whose_time_is_within_a_day_of_the_agents_clock()
    {
        PairedFolder folder = PairedFolder.Create();
        DateTimeOffset old = TestTimes.Start.AddDays(-100);
        string removed = $@"delete audit\{FileOf(old)}";
        folder.Data.Audit.Seed(FileOf(old), Started(old));
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));

        (await host.NextPollAsync()).AnswerBare(503);
        await host.PassAsync(TimeSpan.FromSeconds(3));
        (await host.NextPollAsync()).AnswerPoll(host.Server.Ordinary() with { Time = host.Clock.GetUtcNow().AddDays(2) });
        (await host.NextPollAsync()).AnswerPoll(host.Server.Ordinary());
        await host.Data.WaitForAsync(change => change.Line == removed);

        List<string> operations = Operations.Since(host.Data, 0);
        int thirdPoll = operations.Select((operation, index) => (operation, index)).Where(item => item.operation == "request poll").ElementAt(2).index;
        Assert.Equal(1, operations.Count(operation => operation == removed));
        Assert.True(operations.IndexOf(removed) > thirdPoll, "A file was removed before the answer whose time is the clock's.");
    }

    private static List<JsonElement> Connections(TestHost host)
    {
        return [.. AuditEntries.AppendedSince(host.Data, 0).Where(entry => AuditEntries.Kind(entry) == "connection")];
    }

    private static string? Reason(JsonElement entry)
    {
        return entry.TryGetProperty("reason", out JsonElement reason) && reason.ValueKind == JsonValueKind.String
            ? reason.GetString()
            : null;
    }
}

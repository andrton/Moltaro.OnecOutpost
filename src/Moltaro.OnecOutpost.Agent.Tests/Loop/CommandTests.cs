using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.Agent.Tests.RunningAgent;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeHttp;
using Moltaro.OnecOutpost.TestSupport.FakeWorkers;

namespace Moltaro.OnecOutpost.Agent.Tests.Loop;

// What the loop does with the commands of an answer (concept rules 1, 2, 3 and 10, §7 and §9 of docs/protocol.md, the
// S6 brief, "AgentLoop.RunAsync"): each command in order is refused with its code, or received and then run, or left
// alone when the log knows it; every one is recorded before the next poll opens, and nothing runs before its received
// is on the disk. A run that ends gets its outcome file and its ended, then frees its worker; the outcome is sent, and
// a verified 200 records confirmed before the file is removed.
public sealed class CommandTests
{
    // Rows 1 and 2 of the table of §9: the verified 200 to a poll is acted on, and the verified 200 to an outcome records
    // confirmed and removes the outcome from the outbox. The executor's object goes byte for byte (§8). The outcome is
    // sent once it is recorded, and the worker is free again from then: the poll that opens after it declares it.
    [Fact]
    public async Task A_command_is_received_before_it_runs_and_its_outcome_is_recorded_sent_and_confirmed_before_its_file_goes()
    {
        PairedFolder folder = PairedFolder.Create();
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));
        OutpostCommandMessage read = TestCommands.Of(Guid.NewGuid(), OutpostModeEnum.Read, TestTimes.Start);

        (await host.NextPollAsync()).AnswerPoll(host.Server.Ordinary(host.Server.Sign(read)));
        ServerExchange second = await host.NextPollAsync();
        ServerExchange outcome = await host.NextOutcomeAsync();

        Assert.Equal(read.Id, outcome.ReadOutcome().Command);
        Assert.Equal(TestOutcomes.Succeeded(), outcome.OutcomeBytes());

        second.AnswerPoll(host.Server.Ordinary());
        Assert.Equal(1, (await host.NextPollAsync()).ReadPoll().Bases[0].Read.Free);

        outcome.Confirm();
        await host.Data.WaitForAsync(change => change.Line == $"delete {Operations.OutboxFile(read.Id)}");

        Operations.AssertInOrder(
            Operations.Since(host.Data, 0),
            $"received {read.Id}",
            "run accounting read 1",
            $"replace {Operations.OutboxFile(read.Id)}",
            $"ended {read.Id}",
            "request outcome",
            $"confirmed {read.Id}",
            $"delete {Operations.OutboxFile(read.Id)}");
    }

    // Rule 10: the agent records every command it received before it opens its next poll, which lists them.
    [Fact]
    public async Task Every_command_of_an_answer_is_recorded_before_the_next_poll_opens_and_each_before_it_runs()
    {
        PairedFolder folder = PairedFolder.Create();
        FakeWorkerLauncher workers = new(folder.Data) { Runs = _ => FakeRun.Overruns() };
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start), workers);
        OutpostCommandMessage accounting = TestCommands.Of(Guid.NewGuid(), OutpostModeEnum.Read, TestTimes.Start);
        OutpostCommandMessage warehouse = TestCommands.Of(Guid.NewGuid(), OutpostModeEnum.Read, TestTimes.Start) with { Base = "warehouse" };

        (await host.NextPollAsync()).AnswerPoll(host.Server.Ordinary(host.Server.Sign(accounting), host.Server.Sign(warehouse)));
        OutpostPollRequest second = (await host.NextPollAsync()).ReadPoll();

        Operations.AssertInOrder(
            Operations.Since(host.Data, 0),
            "request poll",
            $"received {accounting.Id}",
            "run accounting read 1",
            $"received {warehouse.Id}",
            "run warehouse read 1",
            "request poll");
        Assert.Equal(new[] { accounting.Id, warehouse.Id }.Order(), second.Received.Order());
        Assert.Equal(0, second.Bases[0].Read.Free);
        Assert.Equal(1, second.Bases[0].Write!.Free);
        Assert.Equal(0, second.Bases[1].Read.Free);
    }

    // Rule 2: a command that cannot be recorded does not run. The write of its received fails, and the agent stops.
    [Fact]
    public async Task A_command_whose_received_cannot_be_written_does_not_run_and_the_agent_stops()
    {
        PairedFolder folder = PairedFolder.Create();
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));
        OutpostCommandMessage read = TestCommands.Of(Guid.NewGuid(), OutpostModeEnum.Read, TestTimes.Start);

        // The first answer records connected and ends the counting; the second has nothing to record before the command.
        (await host.NextPollAsync()).AnswerPoll(host.Server.Ordinary());
        ServerExchange second = await host.NextPollAsync();
        host.Data.Audit.FailNextWrite(bytesKept: 10);
        second.AnswerPoll(host.Server.Ordinary(host.Server.Sign(read)));

        Assert.Equal(AgentExitCodes.Error, await host.ExitAsync());
        List<string> operations = Operations.Since(host.Data, 0);
        Assert.DoesNotContain(operations, operation => operation.StartsWith("run ", StringComparison.Ordinal));
        Assert.DoesNotContain($"received {read.Id}", operations);
    }

    // Rule 1 and §7: the code reaches the audit log and the outcome that the agent sends; protocol-version and malformed
    // name the command alone. A refused command is listed as received until its outcome is confirmed.
    [Theory]
    [InlineData("not-addressed", "accounting", "read")]
    [InlineData("not-served", "payroll", "read")]
    [InlineData("clock-skew", "accounting", "read")]
    [InlineData("expired", "accounting", "read")]
    [InlineData("protocol-version", null, null)]
    [InlineData("malformed", null, null)]
    public async Task A_command_that_fails_a_check_of_rule_1_is_refused_with_its_code_in_the_audit_log_and_in_the_outcome_sent(
        string code,
        string? baseKey,
        string? mode)
    {
        PairedFolder folder = PairedFolder.Create();
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));
        Guid id = Guid.NewGuid();

        (await host.NextPollAsync()).AnswerPoll(host.Server.Ordinary(Failing(host.Server, id, code)));
        ServerExchange outcome = await host.NextOutcomeAsync();
        ServerExchange second = await host.NextPollAsync();

        Assert.Equal(id, outcome.ReadOutcome().Command);
        Assert.Equal(TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Refused, code), outcome.OutcomeBytes());
        JsonElement refused = Assert.Single(AuditEntries.AppendedSince(host.Data, 0), entry => AuditEntries.Kind(entry) == "refused");
        Assert.Equal(id.ToString("D"), refused.GetProperty("command").GetString());
        Assert.Equal(baseKey, refused.GetProperty("base").GetString());
        Assert.Equal(mode, refused.GetProperty("mode").GetString());
        Assert.Equal(code, refused.GetProperty("code").GetString());
        Assert.DoesNotContain(Operations.Since(host.Data, 0), operation => operation.StartsWith("run ", StringComparison.Ordinal));
        Assert.Contains(id, second.ReadPoll().Received);
    }

    // Rule 1: nothing in bytes that do not verify can be trusted, and verified bytes without a protocol and an id name
    // no command: both are only recorded, with nothing for the command and nothing reported.
    [Theory]
    [InlineData("signature")]
    [InlineData("form")]
    public async Task A_command_that_cannot_be_trusted_or_names_no_command_is_only_recorded(string code)
    {
        PairedFolder folder = PairedFolder.Create();
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));
        OutpostSignedCommand command = code == "signature"
            ? FakeServer.Forge(TestCommands.Of(Guid.NewGuid(), OutpostModeEnum.Read, TestTimes.Start))
            : host.Server.Sign("""{"x":1}""");

        (await host.NextPollAsync()).AnswerPoll(host.Server.Ordinary(command));
        ServerExchange second = await host.NextPollAsync();

        JsonElement refused = Assert.Single(AuditEntries.AppendedSince(host.Data, 0), entry => AuditEntries.Kind(entry) == "refused");
        Assert.Equal(JsonValueKind.Null, refused.GetProperty("command").ValueKind);
        Assert.Equal(JsonValueKind.Null, refused.GetProperty("base").ValueKind);
        Assert.Equal(JsonValueKind.Null, refused.GetProperty("mode").ValueKind);
        Assert.Equal(code, refused.GetProperty("code").GetString());
        List<string> operations = Operations.Since(host.Data, 0);
        Assert.DoesNotContain(operations, operation => operation.StartsWith(@"replace outcomes\", StringComparison.Ordinal));
        Assert.DoesNotContain(operations, operation => operation.StartsWith("run ", StringComparison.Ordinal));
        Assert.Empty(second.ReadPoll().Received);
    }

    // Rule 3 and §7: a command whose identity is in the audit log does not run again, and its record stays as it is,
    // whatever an answer that hands it out again holds.
    [Fact]
    public async Task A_command_that_the_audit_log_knows_is_neither_recorded_again_nor_run()
    {
        PairedFolder folder = PairedFolder.Create();
        Guid known = EarlierRun.Ended(folder, OutpostModeEnum.Read, TestOutcomes.Succeeded());
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));
        OutpostCommandMessage again = TestCommands.Of(known, OutpostModeEnum.Read, TestTimes.Start);

        ServerExchange first = await host.NextPollAsync();
        Assert.Contains(known, first.ReadPoll().Received);
        int count = host.Data.Changes.Count;
        first.AnswerPoll(host.Server.Ordinary(host.Server.Sign(again)));
        await host.NextPollAsync();

        List<string> operations = Operations.Since(host.Data, count);
        Assert.DoesNotContain($"received {known}", operations);
        Assert.DoesNotContain($"refused {known}", operations);
        Assert.DoesNotContain($"ended {known}", operations);
        Assert.DoesNotContain(operations, operation => operation.StartsWith("run ", StringComparison.Ordinal));
    }

    // The local policy serves no base payroll, and warehouse only for Read.
    [Theory]
    [InlineData("payroll", "read")]
    [InlineData("warehouse", "write")]
    public async Task A_command_for_a_base_or_mode_that_the_policy_does_not_serve_is_refused_with_not_served_and_the_others_run(
        string baseKey,
        string mode)
    {
        PairedFolder folder = PairedFolder.Create();
        await using TestHost host = TestHost.Start(folder, new FakeClock(TestTimes.Start));
        OutpostCommandMessage unserved = TestCommands.Of(Guid.NewGuid(), TestCommands.ModeOf(mode), TestTimes.Start) with { Base = baseKey };
        OutpostCommandMessage served = TestCommands.Of(Guid.NewGuid(), OutpostModeEnum.Read, TestTimes.Start);

        (await host.NextPollAsync()).AnswerPoll(host.Server.Ordinary(host.Server.Sign(unserved), host.Server.Sign(served)));
        await host.NextPollAsync();

        JsonElement refused = Assert.Single(AuditEntries.AppendedSince(host.Data, 0), entry => AuditEntries.Kind(entry) == "refused");
        Assert.Equal(unserved.Id.ToString("D"), refused.GetProperty("command").GetString());
        Assert.Equal(OutpostOutcomeCodes.NotServed, refused.GetProperty("code").GetString());
        Operations.AssertInOrder(Operations.Since(host.Data, 0), $"received {served.Id}", "run accounting read 1");
    }

    // A command for the agent of PairedFolder that fails the check of the code, signed with the server's key.
    private static OutpostSignedCommand Failing(FakeServer server, Guid id, string code)
    {
        DateTimeOffset now = TestTimes.Start;
        OutpostCommandMessage command = TestCommands.Of(id, OutpostModeEnum.Read, now);
        TimeSpan beyondSkew = command.Skew + TimeSpan.FromSeconds(1);

        return code switch
        {
            "not-addressed" => server.Sign(command with { Agent = Guid.NewGuid() }),
            "not-served" => server.Sign(command with { Base = "payroll" }),
            "clock-skew" => server.Sign(command with { Issued = now + beyondSkew, Deadline = now + beyondSkew + TimeSpan.FromSeconds(30) }),
            "expired" => server.Sign(command with { Issued = now - beyondSkew - TimeSpan.FromSeconds(30), Deadline = now - beyondSkew }),
            "protocol-version" => server.Sign($$"""{"protocol":2,"id":"{{id}}"}"""),
            "malformed" => server.Sign($$"""{"protocol":1,"id":"{{id}}"}"""),
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, "No such check."),
        };
    }
}

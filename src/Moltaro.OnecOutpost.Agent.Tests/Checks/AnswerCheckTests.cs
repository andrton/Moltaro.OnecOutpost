using Moltaro.OnecOutpost.Agent.Checks;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Agent.Tests.Checks.TestChecks;

namespace Moltaro.OnecOutpost.Agent.Tests.Checks;

// The checks of the commands of a poll answer (§6.2 of docs/protocol.md, the S6b brief). Every command is checked by
// §7 before any is recorded (rule 2). The answer is lost when the commands that run are more than the free that the
// poll declared for a base and mode, or when two commands with an identity share it, whatever their verdicts; a
// command that fails §7 counts for nothing. Otherwise the checks come in the order of the answer. So a command never
// waits on the agent for a worker, never runs twice from one answer, and the list of received commands cannot
// overfill. The checks record nothing: the loop records what they give.
public sealed class AnswerCheckTests : IDisposable
{
    private readonly TestChecks _test = new();

    public void Dispose()
    {
        _test.Dispose();
    }

    // §6.2 and the acceptance of S6: a command for a base or mode that the policy does not serve is refused with
    // not-served, a command that fails another check is refused too, a known one is not run again, and the others
    // run. Only those that run count against free: here each base and mode has one free worker.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void An_answer_gives_the_check_of_each_command_in_its_order_and_only_those_that_run_count_against_free()
    {
        OutpostCommandMessage write = Command("accounting", OutpostModeEnum.Write);
        OutpostCommandMessage expired = DeadlineAt(Command("accounting", OutpostModeEnum.Read), Now - Skew - Millisecond);
        OutpostCommandMessage notServed = Command("warehouse", OutpostModeEnum.Write);
        OutpostCommandMessage known = Command("accounting", OutpostModeEnum.Read) with { Id = KnownId };
        OutpostCommandMessage warehouseRead = Command("warehouse", OutpostModeEnum.Read);
        OutpostCommandMessage read = Command("accounting", OutpostModeEnum.Read);
        OutpostCommandMessage[] commands = [write, expired, notServed, known, warehouseRead, read];

        IReadOnlyList<CommandCheck>? checks = _test.Checks.CheckAnswer(
            Poll(accountingRead: 1, accountingWrite: 1, warehouseRead: 1),
            Answer([.. commands.Select(_test.Signed)]),
            _test.Log);

        Assert.NotNull(checks);
        Assert.Equal(commands.Select(command => (Guid?)command.Id), checks.Select(IdOf));
        Assert.Equal(
            [
                CommandVerdictEnum.Runs,
                CommandVerdictEnum.Refused,
                CommandVerdictEnum.Refused,
                CommandVerdictEnum.Known,
                CommandVerdictEnum.Runs,
                CommandVerdictEnum.Runs,
            ],
            checks.Select(check => check.Verdict));
        Assert.Equal([null, "expired", "not-served", null, null, null], checks.Select(check => check.Code));
    }

    // §6.2: when the commands that pass §7 are more than the free of a base and mode, the answer is lost, and none of
    // its commands is recorded or run. The count is by base and mode, whatever the free of the others.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("two Reads where the base declared one worker free for reading")]
    [InlineData("a Write where the base declared none free for writing")]
    public void An_answer_whose_commands_that_run_exceed_the_free_of_a_base_and_mode_is_lost(string form)
    {
        OutpostPollRequest poll;
        OutpostCommandMessage[] commands;

        if (form.StartsWith("two", StringComparison.Ordinal))
        {
            poll = Poll(accountingRead: 1, accountingWrite: 1, warehouseRead: 1);
            commands = [Command("accounting", OutpostModeEnum.Read), Command("accounting", OutpostModeEnum.Read)];
        }
        else
        {
            poll = Poll(accountingRead: 2, accountingWrite: 0, warehouseRead: 1);
            commands = [Command("accounting", OutpostModeEnum.Write)];
        }

        IReadOnlyList<CommandCheck>? checks = _test.Checks.CheckAnswer(
            poll,
            Answer([.. commands.Select(_test.Signed)]),
            _test.Log);

        Assert.Null(checks);
    }

    // §6.2: an answer that holds two commands of one identity is lost, whatever their verdicts, so that a command never
    // runs twice from one answer. The poll has room for both.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("the same command twice")]
    [InlineData("a command, and a command of its identity that is refused")]
    [InlineData("a command that the audit log knows, twice")]
    public void An_answer_with_two_commands_of_one_identity_is_lost_whatever_their_verdicts(string form)
    {
        OutpostCommandMessage read = Command("accounting", OutpostModeEnum.Read);
        OutpostCommandMessage known = Command("accounting", OutpostModeEnum.Read) with { Id = KnownId };
        OutpostSignedCommand[] commands = form switch
        {
            "the same command twice" => [_test.Signed(read), _test.Signed(read)],
            "a command, and a command of its identity that is refused" =>
                [_test.Signed(read), _test.Signed(Command("payroll", OutpostModeEnum.Read) with { Id = read.Id })],
            _ => [_test.Signed(known), _test.Signed(known)],
        };

        IReadOnlyList<CommandCheck>? checks = _test.Checks.CheckAnswer(
            Poll(accountingRead: 2, accountingWrite: 2, warehouseRead: 2),
            Answer(commands),
            _test.Log);

        Assert.Null(checks);
    }

    // Rule 1: a command whose signature does not verify has no identity, so two of the same bytes are not two commands
    // of one identity: each is refused with signature, and the answer is not lost.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void Two_commands_whose_signature_does_not_verify_are_each_refused_and_the_answer_is_not_lost()
    {
        OutpostSignedCommand forged = _test.Forged(Command("accounting", OutpostModeEnum.Read));

        IReadOnlyList<CommandCheck>? checks = _test.Checks.CheckAnswer(
            Poll(accountingRead: 1, accountingWrite: 1, warehouseRead: 1),
            Answer([forged, forged]),
            _test.Log);

        CommandCheck refused = new(CommandVerdictEnum.Refused, null, null, null, "signature", null);
        Assert.NotNull(checks);
        Assert.Equal([refused, refused], checks);
    }

    // §6.2: the ordinary answer without commands, as when the hold ends, is not lost.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void An_answer_without_commands_gives_no_checks_and_is_not_lost()
    {
        IReadOnlyList<CommandCheck>? checks = _test.Checks.CheckAnswer(
            Poll(accountingRead: 1, accountingWrite: 1, warehouseRead: 1),
            Answer([]),
            _test.Log);

        Assert.NotNull(checks);
        Assert.Empty(checks);
    }

    // The S6b brief: the checks record nothing, neither the commands that run nor the refusals; the loop records what
    // they give (rule 2).
    [Fact(Skip = SkipReason.NotImplemented)]
    public void The_checks_of_an_answer_record_nothing()
    {
        OutpostCommandMessage read = Command("accounting", OutpostModeEnum.Read);
        OutpostCommandMessage notServed = Command("payroll", OutpostModeEnum.Read);

        _test.Checks.CheckAnswer(
            Poll(accountingRead: 1, accountingWrite: 1, warehouseRead: 1),
            Answer([_test.Signed(read), _test.Signed(notServed), _test.Forged(Command("accounting", OutpostModeEnum.Write))]),
            _test.Log);

        Assert.Empty(_test.Data.Changes);
        Assert.Null(_test.Log.Find(read.Id));
        Assert.Null(_test.Log.Find(notServed.Id));
    }

    // A poll of the agent as the loop sends it: its bases as the policy of PairedFolder serves them, with the free
    // workers of each mode, and the command of an earlier run as received.
    private static OutpostPollRequest Poll(int accountingRead, int accountingWrite, int warehouseRead)
    {
        TimeSpan runLimit = TimeSpan.FromMinutes(10);

        return new OutpostPollRequest(
            1,
            Now,
            PairedFolder.Version,
            TimeSpan.FromSeconds(25),
            [
                new OutpostPollBase(
                    "accounting",
                    new OutpostPollMode(accountingRead, runLimit),
                    new OutpostPollMode(accountingWrite, runLimit)),
                new OutpostPollBase("warehouse", new OutpostPollMode(warehouseRead, runLimit), null),
            ],
            [KnownId]);
    }

    // An ordinary answer with the commands, as ReadPollAnswer read it.
    private static OutpostPollAnswer Answer(OutpostSignedCommand[] commands)
    {
        return new OutpostPollAnswer(
            Now,
            null,
            TimeSpan.FromSeconds(25),
            TimeSpan.FromSeconds(10),
            ["accounting", "warehouse"],
            [],
            commands);
    }
}

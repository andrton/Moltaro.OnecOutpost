using Moltaro.OnecOutpost.Agent.Audit;
using Moltaro.OnecOutpost.Agent.Outcomes;
using Moltaro.OnecOutpost.Agent.Records;
using Moltaro.OnecOutpost.Agent.Tests.FakeFiles;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Agent.Tests.AuditLines;

namespace Moltaro.OnecOutpost.Agent.Tests.Records;

// What a restart left, as CommandRecords.Open checks it before anything is written (the S6a brief, "The audit log" of
// the plan): an outbox file is read with ReadOutcome for the mode of its command, either mode when no entry names one,
// and must belong to a command that an entry names and agree with what its entries say a refusal was; a command that
// ended, not confirmed, must still have its file. Anything else stops the agent, and Open writes nothing in any case.
public sealed class RestartCheckTests
{
    private static readonly DateTimeOffset Before = TestTimes.Start.AddHours(-1);

    private readonly MemoryDataFolder _data = new();

    private readonly FakeClock _clock = new(TestTimes.Start);

    // Each case leaves the command X in a state from which no start can go on.
    [Theory]
    [InlineData("a file of a command that no entry names", true)]
    [InlineData("a file of a Read that ReadOutcome refuses for a Read", true)]
    [InlineData("a file that is not an outcome object", true)]
    [InlineData("a file of a command with no mode that neither mode allows", true)]
    [InlineData("a refused file of a received command", true)]
    [InlineData("a refused file whose code is not that of its refused", true)]
    [InlineData("a file of another kind for a command that only a refused names", true)]
    [InlineData("an ended without its file and without its confirmed", false)]
    public void Opening_stops_the_agent_on_what_a_start_cannot_finish_and_writes_nothing(string state, bool namesFile)
    {
        Guid command = Guid.NewGuid();
        string file = $"{command}.json";

        switch (state)
        {
            case "a file of a command that no entry names":
                _data.Outcomes.Seed(file, TestOutcomes.Succeeded());
                break;

            case "a file of a Read that ReadOutcome refuses for a Read":
                _data.Audit.Seed(FileOf(Before), Received(Before, command, OutpostModeEnum.Read));
                _data.Outcomes.Seed(file, TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Unknown, "agent-stopped"));
                break;

            case "a file that is not an outcome object":
                _data.Audit.Seed(FileOf(Before), Received(Before, command, OutpostModeEnum.Write));
                _data.Outcomes.Seed(file, "{\"kind\":\"succ");
                break;

            case "a file of a command with no mode that neither mode allows":
                _data.Audit.Seed(FileOf(Before), Ended(Before, command, "unknown", "agent-stopped", null));
                _data.Outcomes.Seed(file, TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Unknown, "disconnected"));
                break;

            case "a refused file of a received command":
                _data.Audit.Seed(FileOf(Before), Received(Before, command, OutpostModeEnum.Write));
                _data.Outcomes.Seed(file, TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Refused, "expired"));
                break;

            case "a refused file whose code is not that of its refused":
                _data.Audit.Seed(FileOf(Before), Refused(Before, command, OutpostModeEnum.Write, "expired"));
                _data.Outcomes.Seed(file, TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Refused, "clock-skew"));
                break;

            case "a file of another kind for a command that only a refused names":
                _data.Audit.Seed(FileOf(Before), Refused(Before, command, OutpostModeEnum.Write, "expired"));
                _data.Outcomes.Seed(file, TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Unknown, "agent-stopped"));
                break;

            default:
                _data.Audit.Seed(
                    FileOf(Before),
                    Received(Before, command, OutpostModeEnum.Write) + Ended(Before, command, "succeeded", null, 5));
                break;
        }

        using AuditLog log = AuditLog.Open(_data.Audit, _clock);
        Outbox outbox = Outbox.Open(_data.Outcomes);
        int count = _data.Changes.Count;

        AgentStopException exception = Assert.Throws<AgentStopException>(() => CommandRecords.Open(log, outbox));

        Assert.Empty(_data.LinesSince(count));

        if (namesFile)
        {
            Assert.Contains($"outcomes\\{file}", exception.Message, StringComparison.Ordinal);
        }
    }

    // Only an ended names the command, as after a retention that removed its received: the file is read for either mode.
    [Fact]
    public void A_file_of_a_command_with_no_mode_is_accepted_when_either_mode_allows_it()
    {
        Guid command = Guid.NewGuid();
        _data.Audit.Seed(FileOf(Before), Ended(Before, command, "unknown", "agent-stopped", null));
        _data.Outcomes.Seed($"{command}.json", TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Unknown, "agent-stopped"));
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);

        CommandRecords.Open(log, Outbox.Open(_data.Outcomes));

        Assert.Empty(_data.Changes);
    }

    // What Recover finishes: a confirmed command with its file, a file that differs from its last ended, a refused
    // without its file and a received with no outcome at all.
    [Fact]
    public void Opening_writes_nothing_for_what_a_restart_left_that_the_start_finishes()
    {
        Guid confirmed = Guid.NewGuid();
        Guid replaced = Guid.NewGuid();
        Guid refused = Guid.NewGuid();
        Guid received = Guid.NewGuid();
        _data.Audit.Seed(
            FileOf(Before),
            Received(Before, confirmed, OutpostModeEnum.Read)
                + Ended(Before, confirmed, "succeeded", null, 5)
                + Confirmed(Before, confirmed)
                + Received(Before, replaced, OutpostModeEnum.Write)
                + Ended(Before, replaced, "succeeded", null, 1234)
                + Refused(Before, refused, OutpostModeEnum.Read, "not-served")
                + Received(Before, received, OutpostModeEnum.Write));
        _data.Outcomes.Seed($"{confirmed}.json", TestOutcomes.Succeeded());
        _data.Outcomes.Seed($"{replaced}.json", TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Unknown, "result-too-large"));
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);

        CommandRecords.Open(log, Outbox.Open(_data.Outcomes));

        Assert.Empty(_data.Changes);
    }
}

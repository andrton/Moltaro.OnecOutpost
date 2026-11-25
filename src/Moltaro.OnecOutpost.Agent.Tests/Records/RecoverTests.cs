using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Audit;
using Moltaro.OnecOutpost.Agent.Outcomes;
using Moltaro.OnecOutpost.Agent.Records;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;
using Moltaro.OnecOutpost.TestSupport.FakeWorkers;
using static Moltaro.OnecOutpost.Agent.Tests.AuditEntries;
using static Moltaro.OnecOutpost.Agent.Tests.AuditLines;

namespace Moltaro.OnecOutpost.Agent.Tests.Records;

// What a start writes after a restart (rule 3): Recover removes the file of a confirmed command, gives a file that
// differs from the last ended or refused of its command, or has neither, an ended that matches it, writes again the
// file of a refused that is not confirmed, and gives a received with neither file nor ended the file of Unknown
// agent-stopped for a Write or Failed lost for a Read, then its ended. An ended that a start writes carries the
// duration of the last ended, or null, since the start did not see the run. Afterwards every outbox file belongs to
// an unconfirmed command and matches its last entry. The loop resends those files.
public sealed class RecoverTests
{
    private static readonly DateTimeOffset Before = TestTimes.Start.AddHours(-1);

    private readonly MemoryDataFolder _data = new();

    private readonly FakeClock _clock = new(TestTimes.Start);

    [Fact]
    public void The_file_of_a_confirmed_command_is_removed()
    {
        Guid command = Guid.NewGuid();
        _data.Audit.Seed(
            FileOf(Before),
            Received(Before, command, OutpostModeEnum.Write)
                + Ended(Before, command, "succeeded", null, 5)
                + Confirmed(Before, command));
        _data.Outcomes.Seed($"{command}.json", TestOutcomes.Succeeded());

        Assert.Equal([$"delete outcomes\\{command}.json"], Recover());
    }

    // A stop between the replacement of a file for its size and its second ended.
    [Fact]
    public void A_file_whose_kind_and_code_differ_from_the_last_ended_gets_an_ended_that_matches_it_with_the_duration_of_that_ended()
    {
        Guid command = Guid.NewGuid();
        _data.Audit.Seed(
            FileOf(Before),
            Received(Before, command, OutpostModeEnum.Write) + Ended(Before, command, "succeeded", null, 1234));
        _data.Outcomes.Seed($"{command}.json", TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Unknown, "result-too-large"));
        int count = _data.Changes.Count;

        Assert.Equal(["append audit\\2026-11-22.jsonl"], Recover());

        AssertEnded(AppendedSince(_data, count).Single(), command, "unknown", "result-too-large", 1234);
    }

    // An outbox file without its ended is recorded. A stop between the file and its ended.
    [Fact]
    public void An_outbox_file_without_its_ended_gets_an_ended_that_matches_it_with_no_duration()
    {
        Guid command = Guid.NewGuid();
        _data.Audit.Seed(FileOf(Before), Received(Before, command, OutpostModeEnum.Read));
        _data.Outcomes.Seed($"{command}.json", TestOutcomes.ScriptError());
        int count = _data.Changes.Count;

        Assert.Equal(["append audit\\2026-11-22.jsonl"], Recover());

        AssertEnded(AppendedSince(_data, count).Single(), command, "failed", "script-error", null);
        Assert.Equal(TestOutcomes.ScriptError(), _data.Outcomes.Bytes($"{command}.json"));
    }

    // A stop between a refused and its file: the Refused outcome is wholly given by its code and identity.
    [Fact]
    public void A_refused_command_without_its_file_gets_its_file_again_from_its_code()
    {
        Guid command = Guid.NewGuid();
        _data.Audit.Seed(FileOf(Before), Refused(Before, command, OutpostModeEnum.Write, "clock-skew"));

        Assert.Equal([$"replace outcomes\\{command}.json"], Recover());
        Assert.Equal(
            TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Refused, "clock-skew"),
            _data.Outcomes.Bytes($"{command}.json"));
    }

    // Under concept rule 3, a received command without an outcome is Unknown agent-stopped for a Write and
    // Failed lost for a Read; its file comes before its ended.
    [Theory]
    [InlineData("write", "unknown", "agent-stopped")]
    [InlineData("read", "failed", "lost")]
    public void A_received_command_without_an_outcome_gets_the_file_of_its_mode_then_its_ended_with_no_duration(
        string mode,
        string outcomeWord,
        string code)
    {
        Guid command = Guid.NewGuid();
        _data.Audit.Seed(FileOf(Before), Received(Before, command, TestCommands.ModeOf(mode)));
        int count = _data.Changes.Count;

        Assert.Equal([$"replace outcomes\\{command}.json", "append audit\\2026-11-22.jsonl"], Recover());

        Assert.Equal(TestOutcomes.Own(TestOutcomes.KindOf(outcomeWord), code), _data.Outcomes.Bytes($"{command}.json"));
        AssertEnded(AppendedSince(_data, count).Single(), command, outcomeWord, code, null);
    }

    [Fact]
    public void What_already_matches_its_last_entry_is_left_as_it_is()
    {
        Guid sent = Guid.NewGuid();
        Guid confirmed = Guid.NewGuid();
        Guid refused = Guid.NewGuid();
        _data.Audit.Seed(
            FileOf(Before),
            Received(Before, sent, OutpostModeEnum.Write)
                + Ended(Before, sent, "succeeded", null, 5)
                + Received(Before, confirmed, OutpostModeEnum.Read)
                + Ended(Before, confirmed, "succeeded", null, 6)
                + Confirmed(Before, confirmed)
                + Refused(Before, refused, OutpostModeEnum.Read, "expired")
                + Refused(Before, null, null, "signature"));
        _data.Outcomes.Seed($"{sent}.json", TestOutcomes.Succeeded());
        _data.Outcomes.Seed($"{refused}.json", TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Refused, "expired"));

        Assert.Empty(Recover());
    }

    // Afterwards every outbox file belongs to a command that is not confirmed and matches its last entry, so the next
    // start finds nothing more to write.
    [Fact]
    public void After_a_recover_every_outbox_file_matches_its_unconfirmed_command_and_the_next_start_writes_nothing()
    {
        Guid confirmed = Guid.NewGuid();
        Guid replaced = Guid.NewGuid();
        Guid refused = Guid.NewGuid();
        Guid write = Guid.NewGuid();
        Guid read = Guid.NewGuid();
        _data.Audit.Seed(
            FileOf(Before),
            Received(Before, confirmed, OutpostModeEnum.Read)
                + Ended(Before, confirmed, "succeeded", null, 5)
                + Confirmed(Before, confirmed)
                + Received(Before, replaced, OutpostModeEnum.Write)
                + Ended(Before, replaced, "succeeded", null, 1234)
                + Refused(Before, refused, OutpostModeEnum.Read, "not-served")
                + Received(Before, write, OutpostModeEnum.Write)
                + Received(Before, read, OutpostModeEnum.Read));
        _data.Outcomes.Seed($"{confirmed}.json", TestOutcomes.Succeeded());
        _data.Outcomes.Seed($"{replaced}.json", TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Unknown, "result-too-large"));

        Recover();

        using (AuditLog log = AuditLog.Open(_data.Audit, _clock))
        {
            Outbox outbox = Outbox.Open(_data.Outcomes);

            Assert.Equal(new[] { replaced, refused, write, read }.Order(), outbox.Commands.Order());

            foreach (Guid command in outbox.Commands)
            {
                AuditCommand facts = log.Find(command)!;
                OutpostOutcomeMessage outcome = OutpostCodec.ReadOutcome(facts.Mode, outbox.Read(command));

                Assert.False(facts.Confirmed);
                Assert.Equal(facts.Kind, outcome.Kind);
                Assert.Equal(facts.Code, outcome.Code);
            }
        }

        Assert.Empty(Recover());
    }

    // §13: a stop in the middle leaves what the next start finishes. Here the ended after the file fails to be written.
    [Fact]
    public void A_stop_in_the_middle_of_a_recover_leaves_what_the_next_start_finishes()
    {
        Guid command = Guid.NewGuid();
        _data.Audit.Seed(FileOf(Before), Received(Before, command, OutpostModeEnum.Write));

        using (AuditLog log = AuditLog.Open(_data.Audit, _clock))
        {
            CommandRecords records = CommandRecords.Open(log, Outbox.Open(_data.Outcomes));
            _data.Audit.FailNextWrite(bytesKept: 0);

            Assert.Throws<AgentStopException>(records.Recover);
        }

        _data.Audit.Heal();
        int count = _data.Changes.Count;

        Assert.Equal(["append audit\\2026-11-22.jsonl"], Recover());

        Assert.Equal(
            TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Unknown, "agent-stopped"),
            _data.Outcomes.Bytes($"{command}.json"));
        AssertEnded(AppendedSince(_data, count).Single(), command, "unknown", "agent-stopped", null);
    }

    // A start on the data folder as it is: the log, the outbox, the check of what the restart left and Recover. The
    // lines of the changes that Recover made.
    private List<string> Recover()
    {
        using AuditLog log = AuditLog.Open(_data.Audit, _clock);
        CommandRecords records = CommandRecords.Open(log, Outbox.Open(_data.Outcomes));
        int count = _data.Changes.Count;

        records.Recover();

        return [.. _data.LinesSince(count)];
    }

    private static void AssertEnded(JsonElement entry, Guid command, string outcome, string code, long? durationMs)
    {
        Assert.Equal("ended", Kind(entry));
        Assert.Equal(command.ToString(), entry.GetProperty("command").GetString());
        Assert.Equal(outcome, entry.GetProperty("outcome").GetString());
        Assert.Equal(code, entry.GetProperty("code").GetString());

        JsonElement duration = entry.GetProperty("durationMs");

        if (durationMs is long expected)
        {
            Assert.Equal(expected, duration.GetInt64());
        }
        else
        {
            Assert.Equal(JsonValueKind.Null, duration.ValueKind);
        }
    }
}

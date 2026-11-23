using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Audit;
using Moltaro.OnecOutpost.Agent.Outcomes;
using Moltaro.OnecOutpost.Agent.Records;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;
using static Moltaro.OnecOutpost.Agent.Tests.AuditEntries;

namespace Moltaro.OnecOutpost.Agent.Tests.Records;

// The order of the records of a command across the audit log and the outbox (rules 2, 3 and 11, the S6a brief):
// received, then the outbox file, then ended, then confirmed, and the file removed last; a refused before the file of
// its Refused outcome; a replacement for size before its second ended. The outcome of the executor is kept as its bytes
// came; an outcome that the agent writes itself is OutpostCodec.Write of one with no result, no error text and no
// messages.
public sealed class CommandOrderTests : IDisposable
{
    private readonly MemoryDataFolder _data = new();

    private readonly AuditLog _log;

    private readonly CommandRecords _records;

    public CommandOrderTests()
    {
        _log = AuditLog.Open(_data.Audit, new FakeClock(TestTimes.Start));
        _records = CommandRecords.Open(_log, Outbox.Open(_data.Outcomes));
    }

    public void Dispose()
    {
        _log.Dispose();
    }

    // The acceptance of S6: received, then the outbox file, then ended, then confirmed.
    [Fact]
    public void A_command_is_recorded_received_then_its_outbox_file_then_ended_then_confirmed_and_its_file_goes_last()
    {
        Guid command = Guid.NewGuid();
        byte[] outcome = TestOutcomes.Succeeded();

        _log.Received(TestCommands.Of(command, OutpostModeEnum.Write, TestTimes.Start));
        _records.End(command, outcome, 812);
        _records.Confirm(command);

        Assert.Equal(
            [
                "append audit\\2026-11-22.jsonl",
                $"replace outcomes\\{command}.json",
                "append audit\\2026-11-22.jsonl",
                "append audit\\2026-11-22.jsonl",
                $"delete outcomes\\{command}.json",
            ],
            _data.LinesSince(0));
        Assert.Equal(["received", "ended", "confirmed"], KindsSince(_data, 0));
        Assert.Equal(outcome, _data.Changes[1].Bytes);
    }

    // The executor's object keeps its own form: the agent forwards its bytes (§8 of docs/protocol.md).
    [Theory]
    [InlineData("read", "succeeded", "succeeded", null)]
    [InlineData("read", "script-error", "failed", "script-error")]
    [InlineData("write", "transaction-ended", "unknown", "transaction-ended")]
    public void End_keeps_the_bytes_of_the_outcome_and_records_the_kind_and_the_code_they_hold_for_the_mode(
        string mode,
        string outcomeName,
        string outcomeWord,
        string? code)
    {
        Guid command = Guid.NewGuid();
        byte[] outcome = ExecutorOutcome(outcomeName);
        _log.Received(TestCommands.Of(command, TestCommands.ModeOf(mode), TestTimes.Start));
        int count = _data.Changes.Count;

        _records.End(command, outcome, 812);

        Assert.Equal(outcome, _data.Outcomes.Bytes($"{command}.json"));

        JsonElement ended = AppendedSince(_data, count).Single();
        Assert.Equal("ended", Kind(ended));
        Assert.Equal(command.ToString(), ended.GetProperty("command").GetString());
        Assert.Equal(outcomeWord, ended.GetProperty("outcome").GetString());
        Assert.Equal(code, ended.GetProperty("code").GetString());
        Assert.Equal(812, ended.GetProperty("durationMs").GetInt64());
    }

    // The loop checks the executor's object by §8 before it ends a command, so bytes that ReadOutcome refuses for the
    // mode are a defect of the agent, whose exception passes: here an Unknown for a Read.
    [Fact]
    public void End_of_an_outcome_that_the_mode_does_not_allow_is_a_defect_whose_exception_passes()
    {
        Guid command = Guid.NewGuid();
        _log.Received(TestCommands.Of(command, OutpostModeEnum.Read, TestTimes.Start));

        Assert.Throws<OutpostProtocolException>(
            () => _records.End(command, TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Unknown, "agent-stopped"), 5));
    }

    // A Refused outcome is wholly given by its code and identity, so the entry comes first and a start can finish the
    // file (the coordinator's decision under PQ2).
    [Fact]
    public void Refuse_records_refused_then_the_file_of_its_Refused_outcome()
    {
        Guid command = Guid.NewGuid();

        _records.Refuse(command, "accounting", OutpostModeEnum.Write, "expired");

        Assert.Equal(["append audit\\2026-11-22.jsonl", $"replace outcomes\\{command}.json"], _data.LinesSince(0));
        Assert.Equal(
            TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Refused, "expired"),
            _data.Outcomes.Bytes($"{command}.json"));

        JsonElement refused = AppendedSince(_data, 0).Single();
        Assert.Equal("refused", Kind(refused));
        Assert.Equal(command.ToString(), refused.GetProperty("command").GetString());
        Assert.Equal("accounting", refused.GetProperty("base").GetString());
        Assert.Equal("write", refused.GetProperty("mode").GetString());
        Assert.Equal("expired", refused.GetProperty("code").GetString());
    }

    // Rule 1: a command whose signature does not verify, or whose verified bytes name no command, has no identity that a
    // refusal could answer, so nothing is reported for it.
    [Theory]
    [InlineData("signature")]
    [InlineData("form")]
    public void A_refusal_without_an_identity_records_only_its_entry(string code)
    {
        _records.Refuse(null, null, null, code);

        Assert.Equal(["append audit\\2026-11-22.jsonl"], _data.LinesSince(0));
        Assert.Equal(code, AppendedSince(_data, 0).Single().GetProperty("code").GetString());
        Assert.Empty(_data.Outcomes.Names());
    }

    // Rule 11: an outcome refused for its size is replaced with Unknown result-too-large for a Write and Failed
    // result-too-large for a Read, recorded by a second ended with the duration of the first.
    [Theory]
    [InlineData("write", "succeeded", "unknown")]
    [InlineData("read", "script-error", "failed")]
    public void ReplaceForSize_writes_the_file_of_result_too_large_then_a_second_ended_with_the_duration_of_the_first(
        string modeWord,
        string outcomeName,
        string outcomeWord)
    {
        Guid command = Guid.NewGuid();
        OutpostModeEnum mode = TestCommands.ModeOf(modeWord);
        OutpostAgentOutcomeKindEnum kind = TestOutcomes.KindOf(outcomeWord);
        _log.Received(TestCommands.Of(command, mode, TestTimes.Start));
        _records.End(command, ExecutorOutcome(outcomeName), 812);
        int count = _data.Changes.Count;

        _records.ReplaceForSize(command);

        Assert.Equal([$"replace outcomes\\{command}.json", "append audit\\2026-11-22.jsonl"], _data.LinesSince(count));
        Assert.Equal(TestOutcomes.Own(kind, "result-too-large"), _data.Outcomes.Bytes($"{command}.json"));

        JsonElement ended = AppendedSince(_data, count).Single();
        Assert.Equal("ended", Kind(ended));
        Assert.Equal(outcomeWord, ended.GetProperty("outcome").GetString());
        Assert.Equal("result-too-large", ended.GetProperty("code").GetString());
        Assert.Equal(812, ended.GetProperty("durationMs").GetInt64());
        Assert.Equal(
            new AuditCommand(command, mode, true, kind, "result-too-large", 812, false),
            _log.Find(command));
    }

    private static byte[] ExecutorOutcome(string name)
    {
        return name switch
        {
            "succeeded" => TestOutcomes.Succeeded(),
            "script-error" => TestOutcomes.ScriptError(),
            _ => TestOutcomes.TransactionEnded(),
        };
    }
}

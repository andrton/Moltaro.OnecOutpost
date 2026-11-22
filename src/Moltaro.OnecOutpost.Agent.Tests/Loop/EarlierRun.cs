using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using static Moltaro.OnecOutpost.Agent.Tests.AuditLines;

namespace Moltaro.OnecOutpost.Agent.Tests.Loop;

// What an earlier run of the agent of a PairedFolder left in its data folder, five minutes before TestTimes.Start, as
// a restart finds it: commands received, ended with their outcome in the outbox, or refused, none of them confirmed.
// Each is written by hand in the forms of the audit log and the outbox (AuditLines), so that a test of the loop starts
// with outcomes to send.
internal static class EarlierRun
{
    public static readonly DateTimeOffset Time = TestTimes.Start.AddMinutes(-5);

    // The duration that the ended entries of the earlier run record.
    public const long DurationMs = 12;

    // A command that ran and ended with this outcome, which waits in the outbox for its confirmation.
    public static Guid Ended(PairedFolder folder, OutpostModeEnum mode, byte[] outcome)
    {
        Guid id = Guid.NewGuid();
        OutpostOutcomeMessage message = OutpostCodec.ReadOutcome(mode, outcome);

        Append(folder, Received(Time, id, mode) + AuditLines.Ended(Time, id, Word(message.Kind), message.Code, DurationMs));
        folder.Data.Outcomes.Seed($"{id}.json", outcome);

        return id;
    }

    // A command received and never ended, as after a stop while it ran.
    public static Guid ReceivedOnly(PairedFolder folder, OutpostModeEnum mode)
    {
        Guid id = Guid.NewGuid();

        Append(folder, Received(Time, id, mode));

        return id;
    }

    // A Read refused with the code, its Refused outcome in the outbox.
    public static Guid Refused(PairedFolder folder, string code)
    {
        Guid id = Guid.NewGuid();

        Append(folder, AuditLines.Refused(Time, id, OutpostModeEnum.Read, code));
        folder.Data.Outcomes.Seed($"{id}.json", TestOutcomes.Own(OutpostAgentOutcomeKindEnum.Refused, code));

        return id;
    }

    // Adds lines to the audit file of the earlier run.
    public static void Append(PairedFolder folder, string lines)
    {
        string name = FileOf(Time);
        string before = folder.Data.Audit.Has(name) ? folder.Data.Audit.Text(name) : "";

        folder.Data.Audit.Seed(name, before + lines);
    }

    // The word of an outcome kind in ended: succeeded, failed or unknown.
    public static string Word(OutpostAgentOutcomeKindEnum kind)
    {
        return kind.ToString().ToLowerInvariant();
    }
}

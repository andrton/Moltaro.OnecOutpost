using Moltaro.OnecOutpost.Agent.Outcomes;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Workers;

// The judgment of what a COM worker gave within its run limit (§8 of docs/protocol.md), for the pool and for any run
// of a worker outside it.
internal static class WorkerOutcome
{
    // The outcome sent: the bytes of the executor, or of the COM worker itself, as they came when the agent may accept
    // them, and otherwise an outcome of the agent's own. An outcome is accepted only when the size the executor measured
    // is the length of the bytes received, ReadOutcome reads it for the mode and IsComWorkerOutcome allows its kind and
    // code; anything else is result-type, Failed for a Read and Unknown for a Write. The worker ends when it ended, or
    // broke its line, without an outcome, and when it gave base-unreachable or connection-lost, after which the COM
    // worker ends.
    public static JudgedOutcome Judge(OutpostModeEnum mode, (long Size, byte[] Outcome)? given)
    {
        if (given is not (long size, byte[] outcome))
        {
            return new JudgedOutcome(OwnOutcome.AfterStart(mode, OutpostOutcomeCodes.ConnectionLost), true);
        }

        OutpostOutcomeMessage? message = ReadOrNull(mode, outcome);
        bool ends = message?.Code is OutpostOutcomeCodes.BaseUnreachable or OutpostOutcomeCodes.ConnectionLost;

        bool accepted = size == outcome.Length
            && message is not null
            && OutpostOutcomeCodes.IsComWorkerOutcome(mode, message.Kind, message.Code);

        return new JudgedOutcome(accepted ? outcome : OwnOutcome.AfterStart(mode, OutpostOutcomeCodes.ResultType), ends);
    }

    // The object of §8 that the bytes hold for the mode, or null when ReadOutcome refuses them.
    private static OutpostOutcomeMessage? ReadOrNull(OutpostModeEnum mode, byte[] outcome)
    {
        try
        {
            return OutpostCodec.ReadOutcome(mode, outcome);
        }
        catch (OutpostProtocolException)
        {
            return null;
        }
    }
}

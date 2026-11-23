using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Outcomes;

// The outcomes that the agent writes itself, for the pool of workers and the records of a command alike: no result,
// no error text and no messages.
internal static class OwnOutcome
{
    public static byte[] Write(OutpostAgentOutcomeKindEnum kind, string code)
    {
        return OutpostCodec.Write(new OutpostOutcomeMessage(kind, code, null, null, [], false));
    }

    // The outcome for a command that may have run, of the kind that KindAfterStart gives.
    public static byte[] AfterStart(OutpostModeEnum mode, string code)
    {
        return Write(KindAfterStart(mode), code);
    }

    // The kind for a command that may have run: Unknown for a Write, whose script may have committed (rule 5), and
    // Failed for a Read, or for a command whose mode no entry of the audit log names.
    public static OutpostAgentOutcomeKindEnum KindAfterStart(OutpostModeEnum? mode)
    {
        return mode == OutpostModeEnum.Write ? OutpostAgentOutcomeKindEnum.Unknown : OutpostAgentOutcomeKindEnum.Failed;
    }
}

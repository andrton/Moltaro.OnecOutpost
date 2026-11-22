using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Server.Store;

// What the checks of an outcome need of its command (§6.3 of docs/protocol.md): its mode and its outcome limit.
internal sealed record CommandLimits(OutpostModeEnum Mode, int OutcomeLimit);

namespace Moltaro.OnecOutpost.Server.Store;

// How far a command came towards its agent. It only moves forward: Waiting, never handed out; HandedOut, in at least
// one poll answer; Received, listed in `received` by a newest poll of its agent (rules 10, 11). An outcome the agent
// sent does not change it.
internal enum CommandPhaseEnum
{
    Waiting = 0,
    HandedOut,
    Received,
}

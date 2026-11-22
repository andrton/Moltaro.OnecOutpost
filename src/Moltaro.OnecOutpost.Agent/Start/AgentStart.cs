using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Agent.Secrets;

namespace Moltaro.OnecOutpost.Agent.Start;

// What a start finds in the data folder, after a restart included.
internal static class AgentStart
{
    // Runs the steps of a start, and the first that fails decides: the audit log, whose IOException passes and records
    // nothing; a Fault of the log; the access lists of the data folder, its folders and its files; agent.json and
    // secrets.dat; the outbox, the records of commands and the poll numbers; and policy.json, with a password in the
    // secrets for every mode it serves. A failed step after the first records started with the error and
    // withoutSafeMode empty, records policy invalid when policy.json failed, disposes the log and throws
    // AgentStopException with the same reason. When every step passes, it records started with the bases that write
    // without safe mode and policy as valid, calls Recover and returns the run. version is the agent's version as given.
    public static AgentRun Open(
        AgentFolders folders,
        IAccessLists access,
        IMachineProtection protection,
        TimeProvider clock,
        string version)
    {
        throw new NotSupportedException();
    }
}

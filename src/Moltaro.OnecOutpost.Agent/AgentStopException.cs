namespace Moltaro.OnecOutpost.Agent;

// The agent stops, for the reason that its started entry records. The reason names the cause and, for a file, its path
// in the data folder, such as outcomes\{id}.json; it never quotes a value read from a file. It lives in the root
// because every folder of the agent throws it.
internal sealed class AgentStopException : Exception
{
    public AgentStopException(string reason)
        : base(reason)
    {
    }
}

namespace Moltaro.OnecOutpost.Agent.Policy;

// A policy.json that is invalid: the path of the member at fault, such as bases[1].read.workers, and a reason that says
// what is wrong there and what is expected (Q1). The path is empty when the fault is the file as a whole, such as text
// that is not JSON. The agent then serves nothing and records the message in its audit log.
internal sealed class AgentPolicyException : Exception
{
    public AgentPolicyException(string path, string reason)
        : base(path.Length == 0 ? reason : $"{path}: {reason}")
    {
        Path = path;
        Reason = reason;
    }

    public string Path { get; }

    public string Reason { get; }
}

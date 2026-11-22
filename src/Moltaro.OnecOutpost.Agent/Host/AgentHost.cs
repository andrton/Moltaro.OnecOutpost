namespace Moltaro.OnecOutpost.Agent.Host;

// Runs one agent in the process, as the service does and as a test does.
internal static class AgentHost
{
    // AgentStart.Open, whose IOException gives NotStarted and AgentStopException Error; the keys; the pool, its opens
    // started; the connection over Handlers(server, proxy); the checks; the loop, which gives Stopped for the token and
    // Error for AgentStopException. It disposes the pool, the connection and the log, in that order.
    public static Task<int> RunAsync(AgentParts parts, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}

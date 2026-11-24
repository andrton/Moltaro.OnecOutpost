using System.Diagnostics;
using System.Security.Cryptography;
using Moltaro.OnecOutpost.Agent.Checks;
using Moltaro.OnecOutpost.Agent.Loop;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Signing;

namespace Moltaro.OnecOutpost.Agent.Host;

// Runs one agent in the process, as the service does and as a test does.
internal static class AgentHost
{
    // AgentStart.Open, whose IOException gives NotStarted and AgentStopException Error; the keys; the pool, its opens
    // started; the connection over Handlers(server, proxy); the checks; the loop, which gives Stopped for the token,
    // recording nothing, and Error for AgentStopException, after stopped with its reason (the owner's Q16), which the
    // log does not write once a write of it failed. The pool, the checks and the loop serve the policy as the start
    // read it: a base whose write turns safe mode off is served for Write, its workers under the worker account that the
    // start logged on. It disposes the pool, the connection and the log, in that order.
    public static async Task<int> RunAsync(AgentParts parts, CancellationToken cancellationToken)
    {
        AgentRun run;

        try
        {
            run = AgentStart.Open(
                parts.Folders,
                parts.Access,
                parts.Protection,
                parts.Workers,
                parts.ProgramFolder,
                parts.Clock,
                parts.Version);
        }
        catch (IOException)
        {
            // The log was not opened, as for a second copy of the agent: nothing is recorded.
            return AgentExitCodes.NotStarted;
        }
        catch (AgentStopException)
        {
            // Recorded by the start: as started with the error, or, for a fault of its recovery after started, as
            // stopped.
            return AgentExitCodes.Error;
        }

        try
        {
            await RunLoopAsync(parts, run, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return AgentExitCodes.Stopped;
        }
        catch (AgentStopException exception)
        {
            run.Log.Stopped(exception.Message);

            return AgentExitCodes.Error;
        }
        finally
        {
            // Last, so that the lock of the log is held until nothing of the run is left.
            run.Log.Dispose();
        }

        throw new UnreachableException("The loop ends only by its token or by a stop of the agent's own.");
    }

    private static async Task RunLoopAsync(AgentParts parts, AgentRun run, CancellationToken cancellationToken)
    {
        // The secrets file was checked to hold an ECDSA key on P-256 in PKCS#8, and agent.json the server key in the
        // form of §2.
        using ECDsa agentKey = ECDsa.Create();
        agentKey.ImportPkcs8PrivateKey(run.Secrets.PrivateKey, out _);
        using ECDsa serverKey = OutpostKeys.ImportPublicKey(run.Pairing.ServerKey);

        WorkerPool workers = new(run.Policy, run.Secrets, parts.Workers, parts.Executor, parts.Clock);
        AgentConnection? connection = null;

        try
        {
            // The opens run beside the loop, each bounded by the run limit of its mode, so the first poll goes at once.
            workers.Open();

            connection = new AgentConnection(
                run.Pairing.Server,
                run.Pairing.Agent,
                agentKey,
                serverKey,
                parts.Handlers(run.Pairing.Server, run.Policy.Proxy),
                parts.Clock);

            CommandChecks checks = new(run.Pairing.Agent, serverKey, run.Policy, parts.Clock);
            AgentLoop loop = new(run, connection, checks, workers, parts.Clock, parts.Version);

            await loop.RunAsync(cancellationToken);
        }
        finally
        {
            // The workers end first, so that no run outlives the agent; then the handler goes.
            await workers.DisposeAsync();
            connection?.Dispose();
        }
    }
}

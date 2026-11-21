using Moltaro.OnecOutpost.ComWorker.Tests.FakeConnection;

namespace Moltaro.OnecOutpost.ComWorker.Tests;

// One run of Worker.Run in the test process, with the fake of the COM connection behind it: the agent's lines in, the
// standard output and the diagnostics out, and the exit code the worker returned.
internal sealed class WorkerRun
{
    private WorkerRun(int exitCode, AgentInput input, WorkerOutput output, string diagnostics)
    {
        ExitCode = exitCode;
        Input = input;
        Output = output;
        Diagnostics = diagnostics;
    }

    public int ExitCode { get; }

    public AgentInput Input { get; }

    public WorkerOutput Output { get; }

    public string Diagnostics { get; }

    public static WorkerRun Of(WorkerModeEnum mode, FakeConnector connector, params byte[][] lines)
    {
        WorkerOutput output = new();
        AgentInput input = new(output, lines);
        StringWriter diagnostics = new();

        int exitCode = new Worker(connector, mode).Run(input, output, diagnostics);

        return new WorkerRun(exitCode, input, output, diagnostics.ToString());
    }
}

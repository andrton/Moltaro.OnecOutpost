using Moltaro.OnecOutpost.ComWorker.Tests.FakeConnection;

namespace Moltaro.OnecOutpost.ComWorker.Tests;

// One run of Worker.Run in the test process, with the fake of the COM connection behind it: the agent's lines in, the
// standard output and the diagnostics out, with how many lines of the output came before the first diagnostics, and the
// exit code the worker returned.
internal sealed class WorkerRun
{
    private WorkerRun(int exitCode, AgentInput input, WorkerOutput output, WorkerDiagnostics diagnostics)
    {
        ExitCode = exitCode;
        Input = input;
        Output = output;
        Diagnostics = diagnostics.ToString();
        OutputLinesBeforeDiagnostics = diagnostics.OutputLinesBefore;
    }

    public int ExitCode { get; }

    public AgentInput Input { get; }

    public WorkerOutput Output { get; }

    public string Diagnostics { get; }

    // The lines of the output that the worker had written when it wrote its first diagnostics; null without any.
    public int? OutputLinesBeforeDiagnostics { get; }

    public static WorkerRun Of(WorkerModeEnum mode, FakeConnector connector, params byte[][] lines)
    {
        WorkerOutput output = new();
        AgentInput input = new(output, lines);
        WorkerDiagnostics diagnostics = new(output);

        int exitCode = new Worker(connector, mode).Run(input, output, diagnostics);

        return new WorkerRun(exitCode, input, output, diagnostics);
    }
}

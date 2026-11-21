namespace Moltaro.OnecOutpost.ComWorker.Tests.FakeConnection;

// One executor of a fake session: it records how it was opened and every call of Run, and answers each with the answer
// its session gave it. Its events go to the log of its session.
internal sealed class FakeExecutor(int number, string path, bool safeMode, Func<string> answer, List<string> events)
    : IOneCExecutor
{
    // The executors of a session are numbered from 1, in the order in which they were opened.
    public int Number => number;

    public string Path => path;

    public bool SafeMode => safeMode;

    public List<FakeRun> Runs { get; } = [];

    public int DisposeCount { get; private set; }

    public string Run(string mode, string script, string parametersJson, string? callerKey, long outcomeLimit)
    {
        Runs.Add(new FakeRun(mode, script, parametersJson, callerKey, outcomeLimit));
        events.Add($"run {number}");

        return answer();
    }

    public void Dispose()
    {
        DisposeCount++;
        events.Add($"dispose {number}");
    }
}

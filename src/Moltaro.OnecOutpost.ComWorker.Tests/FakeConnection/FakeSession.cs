namespace Moltaro.OnecOutpost.ComWorker.Tests.FakeConnection;

// The one session of a fake connection. Each executor it opens answers its Run with the next of the answers a test
// gave, which returns a text or throws; a command beyond them gets an outcome that succeeded. It records every executor
// it opened and, in Events, the order in which executors were opened, run and disposed.
internal sealed class FakeSession(params Func<string>[] answers) : IOneCSession
{
    private readonly Queue<Func<string>> _answers = new(answers);

    // What OpenExecutor throws, as an executor that does not open.
    public Exception? OpenFailure { get; init; }

    public int OpenAttempts { get; private set; }

    public List<FakeExecutor> Executors { get; } = [];

    // "open 1", "run 1", "dispose 1", "open 2", ...: the executors by the order in which they were opened.
    public List<string> Events { get; } = [];

    public int DisposeCount { get; private set; }

    public IOneCExecutor OpenExecutor(string path, bool safeMode)
    {
        OpenAttempts++;

        if (OpenFailure is not null)
        {
            throw OpenFailure;
        }

        Func<string> answer = _answers.Count > 0 ? _answers.Dequeue() : () => ExecutorLine.Succeeded;
        FakeExecutor executor = new(Executors.Count + 1, path, safeMode, answer, Events);

        Executors.Add(executor);
        Events.Add($"open {executor.Number}");

        return executor;
    }

    public void Dispose()
    {
        DisposeCount++;
    }
}

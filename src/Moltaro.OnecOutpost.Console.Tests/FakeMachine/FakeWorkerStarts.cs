using Moltaro.OnecOutpost.Console.Workers;

namespace Moltaro.OnecOutpost.Console.Tests.FakeMachine;

// The COM workers of check on a test's machine. No command but check starts one, so a run that asks for one fails the
// test with the reason.
internal sealed class FakeWorkerStarts : IWorkerStarts
{
    private const string NoWorker = "This test's machine starts no COM worker.";

    public IWorkerAccount LogOn(string baseKey, string account, string password)
    {
        throw new InvalidOperationException(NoWorker);
    }

    public Task<uint?> ProbeAsync(IWorkerAccount account, string program, string baseKey, TimeSpan deadline, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException(NoWorker);
    }

    public CheckWorker Start(IWorkerAccount? account, string program, string baseKey)
    {
        throw new InvalidOperationException(NoWorker);
    }
}

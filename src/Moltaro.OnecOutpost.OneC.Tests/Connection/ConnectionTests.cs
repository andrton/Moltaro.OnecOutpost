using Moltaro.OnecOutpost.OneC.Tests.Lab;
using Moltaro.OnecOutpost.OneC.Tests.Workers;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.OneC.Tests.Connection;

// The connection of ComConnector to a real base: a session that does not open gives {"ready":false} and exit code 2,
// and an executor that does not open gives Failed base-unreachable and exit code 2 (the brief of S9, "The lines").
public sealed class ConnectionTests
{
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_wrong_password_gives_ready_false_and_ends_the_worker_with_exit_code_2_without_the_password_in_its_output()
    {
        LabTestBase testBase = LabFile.Read().TestBase;
        string wrongPassword = $"wrong-{Guid.NewGuid():N}";
        await using WorkerProcess worker = WorkerProcess.Start("test", "read");

        bool ready = await worker.ConnectAsync(
            LabWorkers.Location(testBase.Server, testBase.Database),
            testBase.Read.User,
            wrongPassword,
            safeMode: true,
            Repository.ExecutorPath);

        Assert.False(ready);
        Assert.Equal(2, await worker.WaitForExitAsync());
        Assert.DoesNotContain(wrongPassword, worker.Diagnostics);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task An_executor_that_does_not_open_gives_failed_base_unreachable_and_ends_the_worker_with_exit_code_2()
    {
        LabTestBase testBase = LabFile.Read().TestBase;
        string missingExecutor = Path.Combine(Path.GetTempPath(), $"outpost-{Guid.NewGuid():N}.epf");
        await using WorkerProcess worker = WorkerProcess.Start("test", "read");

        bool ready = await worker.ConnectAsync(
            LabWorkers.Location(testBase.Server, testBase.Database),
            testBase.Read.User,
            testBase.Read.Password,
            safeMode: true,
            missingExecutor);

        Assert.True(ready, worker.Diagnostics);

        OutcomeLine outcome = await worker.RunAsync("Result = 1;");

        outcome.AssertIs("failed", "base-unreachable");
        Assert.Null(outcome.ErrorText);
        Assert.Equal(2, await worker.WaitForExitAsync());
    }
}

using Moltaro.OnecOutpost.OneC.Tests.Lab;
using Moltaro.OnecOutpost.OneC.Tests.Workers;

namespace Moltaro.OnecOutpost.OneC.Tests.Connection;

// The connection of ComConnector to a real base: a session that does not open gives {"ready":false} and exit code 2,
// and an executor that does not open gives Failed base-unreachable and exit code 2. The agent ends a worker by closing
// its input; the worker then releases its session and ends with exit code 0.
public sealed class ConnectionTests
{
    [Fact]
    public async Task A_worker_whose_input_is_closed_after_a_command_ends_with_exit_code_0()
    {
        await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();

        (await reader.RunAsync("Result = 1;")).AssertIs("succeeded", null);

        Assert.Equal(0, await reader.EndAsync());
    }

    [Fact]
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

    [Fact]
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

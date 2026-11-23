using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.Win32;
using Moltaro.OnecOutpost.Agent.Tests.Workers;
using Moltaro.OnecOutpost.Agent.Workers;

namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// The start of a worker (the S9b brief, "The start"): the program with the base key and the mode as its command line,
// read or write, and nothing else; the connection, with its password, travels only in the first line of its input,
// which the worker gets as ComWorkerProcess wrote it.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class WorkerStartTests
{
    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    public async Task A_worker_gets_the_base_key_and_the_mode_alone_as_its_command_line_and_its_connection_password_included_as_its_first_line(
        string modeWord)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TestConnector connector = TestConnector.Register(RegistryView.Registry64);
        using WorkerJob job = new();
        using ComWorkerLauncher launcher = new(TestProgram.Path, TestProgram.Path, connector.Finder, job);
        WorkerConnectionLine connection = TestLines.Connection();
        using IWorkerProcess worker = launcher.Start("accounting", TestCommands.ModeOf(modeWord));

        Assert.True(await worker.OpenAsync(connection, cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken));
        JsonElement arguments = TestLines.ResultOf(
            await worker.RunAsync(TestLines.Command("arguments"), cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken));
        JsonElement given = TestLines.ResultOf(
            await worker.RunAsync(TestLines.Command("connection"), cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken));

        Assert.Equal(["accounting", modeWord], arguments.EnumerateArray().Select(argument => argument.GetString()));
        Assert.Equal(
            ["executor", "location", "password", "safeMode", "user"],
            given.EnumerateObject().Select(member => member.Name).Order(StringComparer.Ordinal));
        Assert.Equal(connection.Location, given.GetProperty("location").GetString());
        Assert.Equal(connection.User, given.GetProperty("user").GetString());
        Assert.Equal(connection.Password, given.GetProperty("password").GetString());
        Assert.Equal(connection.SafeMode, given.GetProperty("safeMode").GetBoolean());
        Assert.Equal(connection.Executor, given.GetProperty("executor").GetString());
    }
}

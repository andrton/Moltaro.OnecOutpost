using System.Runtime.Versioning;
using Moltaro.OnecOutpost.Console;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.OneC.Tests.Agent;
using Moltaro.OnecOutpost.OneC.Tests.Lab;
using Moltaro.OnecOutpost.TestSupport;
using Spectre.Console.Cli.Testing;
using Spectre.Console.Testing;

namespace Moltaro.OnecOutpost.OneC.Tests.Console;

// The console's check and metadata on a LabAgent, through
// CommandAppTester with ConsoleApp.Configure, as Program runs it. The account that runs the tests stands for the
// agent's account. The console and its in-process server share the machine's clock so the real workers' run and
// shutdown limits can elapse. check's probe needs "Impersonate a client after authentication", so the tests run
// elevated. Each run checks that the program folder can be removed at once and that the server saw no poll.
[SupportedOSPlatform("windows")]
internal static class LabConsole
{
    // The longest a run of check takes on the lab: a worker of each account of the base, each of which may take twenty
    // seconds to open on a Windows account that never used 1C (LabLimits), and the probe.
    public static readonly TimeSpan Deadline = TimeSpan.FromMinutes(10);

    // The longest a run of metadata takes on the lab: the walk of a copy of a typical base, of thousands of objects with
    // their storage, outlasts check's deadline by far.
    public static readonly TimeSpan MetadataDeadline = TimeSpan.FromHours(2);

    private const string ServiceName = "MoltaroOnecOutpost";

    // Runs check on the agent to its end.
    public static async Task<LabCheck> CheckAsync(LabAgent agent)
    {
        CommandAppResult result = await RunAsync(agent, ["check"], Deadline, TestContext.Current.CancellationToken);

        return new LabCheck(result);
    }

    // Runs metadata of the agent's base to its end, its report the file given.
    public static async Task<LabMetadata> MetadataAsync(LabAgent agent, string file)
    {
        CommandAppResult result = await RunAsync(agent, ["metadata", agent.Key, file], MetadataDeadline, TestContext.Current.CancellationToken);

        return new LabMetadata(result, file, named: agent.Key == LabTestBase.Key);
    }

    private static async Task<CommandAppResult> RunAsync(
        LabAgent agent,
        string[] arguments,
        TimeSpan deadline,
        CancellationToken cancellationToken)
    {
        ConsoleParts machine = Program.MachineParts(ServiceName, AppContext.BaseDirectory, agent.ProgramsPath, agent.DataPath);

        Assert.True(machine.Elevated, "The lab console needs administrator rights: run the tests in an elevated terminal.");

        using LabServer server = new(agent.ServerKey, agent.AgentKey, agent.Agent, machine.Clock);
        ConsoleParts parts = machine with
        {
            AgentSid = AccessListEdits.TestAccount.Value,
            Handlers = (_, _) => server,
        };
        CommandAppTester tester = new(console: new TestConsole().Width(int.MaxValue));
        tester.Configure(config => ConsoleApp.Configure(config, parts));

        string command = arguments[0];
        string target = agent.Key == LabTestBase.Key ? "the test base" : "a typical copy";
        TestContext.Current.TestOutputHelper?.WriteLine($"Starting lab {command} on {target} with a deadline of {deadline:c}.");

        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(deadline);
        CommandAppResult result;

        try
        {
            result = await tester.RunAsync(arguments, limit.Token);

            // Spectre can return the console's Cancelled exit code after it has awaited the command's cleanup.
            limit.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException exception) when (limit.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"The lab {command} command exceeded its deadline of {deadline:c}.", exception);
        }
        finally
        {
            agent.RemoveProgramsAfterCommand();
            Assert.Equal(0, server.Polls);
        }

        return result;
    }
}

using Moltaro.OnecOutpost.Agent.Connector;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;

namespace Moltaro.OnecOutpost.Console.Tests.Check;

// The line Connector of check (the S10b brief, "check", and the stage contract): the first registered connector, 64 bits
// first, with which the workers start as the service starts them, every other copy named; and without one, every copy
// that is installed and not registered, with the command that registers it. No worker starts without a registered
// connector, so the probe and the connections are not checked then.
public sealed class ConnectorLineTests : IDisposable
{
    private const string Other64 = "8.3.24.1691";

    private const string Version32 = "8.3.27.1859";

    private readonly CheckedMachine _checked = new();

    private TestMachine Machine => _checked.Machine;

    public void Dispose()
    {
        _checked.Dispose();
    }

    [Fact]
    public async Task With_connectors_of_both_bitnesses_the_line_names_the_registered_one_of_64_bits_and_every_other_copy()
    {
        ConnectorFile registered32 = Machine.Connectors.Registered(ConnectorBitnessEnum.X86, Version32);
        ConnectorFile installed64 = Machine.Connectors.Installed(ConnectorBitnessEnum.X64, Other64);

        CheckRun run = await _checked.RunAsync();

        string reason = run.Passed(CheckLineEnum.Connector);
        string registered64 = FakeConnectors.PathOf(ConnectorBitnessEnum.X64, CheckedMachine.ConnectorVersion);
        Assert.StartsWith(
            $"64-bit: {registered64}, {CheckedMachine.ConnectorVersion}, is registered; the workers start with it",
            reason,
            StringComparison.Ordinal);
        Assert.Contains(registered32.Path, reason, StringComparison.Ordinal);
        Assert.Contains(installed64.Path, reason, StringComparison.Ordinal);
        Assert.All(Machine.Workers.Processes, process => Assert.Equal(CheckedMachine.Program, process.Program));
    }

    // The service starts the worker of the first registered bitness, so check starts that one, and judges its folder.
    [Fact]
    public async Task With_the_connector_registered_for_32_bits_alone_the_workers_start_from_the_x86_folder()
    {
        Machine.Connectors.Uninstall(ConnectorBitnessEnum.X64);
        ConnectorFile registered32 = Machine.Connectors.Registered(ConnectorBitnessEnum.X86, Version32);
        string program = ProgramFolderLayout.WorkerOf(TestMachine.ProgramFolder, ConnectorBitnessEnum.X86);

        CheckRun run = await _checked.RunAsync();

        Assert.StartsWith(
            $"32-bit: {registered32.Path}, {Version32}, is registered; the workers start with it",
            run.Passed(CheckLineEnum.Connector),
            StringComparison.Ordinal);
        Assert.NotEmpty(Machine.Workers.Processes);
        Assert.All(Machine.Workers.Processes, process => Assert.Equal(program, process.Program));
        Assert.Contains($"probe accounting outpost-worker {program} within 00:10:00", Machine.Operations);
        run.Passed(CheckLineEnum.AccessLists, Path.GetDirectoryName(registered32.Path)!);
    }

    [Fact]
    public async Task Without_a_registered_connector_the_line_fails_naming_each_copy_installed_and_no_worker_starts()
    {
        Machine.Connectors.Uninstall(ConnectorBitnessEnum.X64);
        ConnectorFile installed64 = Machine.Connectors.Installed(ConnectorBitnessEnum.X64, Other64);
        ConnectorFile installed32 = Machine.Connectors.Installed(ConnectorBitnessEnum.X86, Version32);

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        string reason = run.Failed(CheckLineEnum.Connector);
        Assert.StartsWith("none is registered", reason, StringComparison.Ordinal);
        Assert.Contains($"64-bit: {installed64.Path}, {Other64}", reason, StringComparison.Ordinal);
        Assert.Contains($"32-bit: {installed32.Path}, {Version32}", reason, StringComparison.Ordinal);
        Assert.EndsWith("; register it with connector register", reason, StringComparison.Ordinal);

        Assert.StartsWith("not checked: ", run.Failed(CheckLineEnum.WorkerStart, "accounting"), StringComparison.Ordinal);
        Assert.StartsWith("not checked: ", run.Failed(CheckLineEnum.Connection, "accounting", "outpost-read"), StringComparison.Ordinal);
        Assert.StartsWith("not checked: ", run.Failed(CheckLineEnum.Connection, "warehouse", "outpost-read"), StringComparison.Ordinal);
        Assert.Empty(Machine.Workers.Processes);
        Assert.DoesNotContain(Machine.Operations, line => line.StartsWith("probe ", StringComparison.Ordinal));
        run.Passed(CheckLineEnum.WorkerAccount, "accounting");
        run.Passed(CheckLineEnum.Server);
    }
}

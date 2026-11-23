using System.Text;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Folders;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.TestSupport;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.DataFolder;

// The steps that pair and secret share, written once (the S10 brief, "The steps pair and secret share", and Q14): after
// the elevation, a data folder that is absent ends the command with NotInstalled, and one that is a reparse point with
// DataFolderAdmitsOthers; the access list of the folder, made for the agent's account, admitting anyone else ends it
// with DataFolderAdmitsOthers too, since DPAPI for the machine lets every local process decrypt and only the folder's
// list keeps the secrets; and policy.json that is absent or invalid ends it with PolicyInvalid and the agent's reason.
// Each ends before the command's own steps: pair sends nothing and secret reads no password.
public sealed class DataFolderStepTests
{
    private readonly TestMachine _machine = new();

    public DataFolderStepTests()
    {
        _machine.Input = new FakeInput(_machine.Data, "a password\n");
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("pair")]
    [InlineData("secret")]
    public async Task A_data_folder_that_is_absent_ends_the_command_with_NotInstalled(string command)
    {
        CommandAppResult result = await _machine.RunAsync(TestMachine.CommandLine(command));

        Assert.Equal(ConsoleExitCodes.NotInstalled, result.ExitCode);
        Assert.Contains("NotInstalled (6): ", result.Output);
        Assert.Contains("run install first", result.Output);
        Assert.Empty(_machine.Operations);
        Assert.Empty(_machine.Routes);
    }

    // A junction in the place of the data folder would lead the secrets into a folder that nobody judged.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("pair")]
    [InlineData("secret")]
    public async Task A_data_folder_that_is_a_reparse_point_ends_the_command_with_DataFolderAdmitsOthers(string command)
    {
        _machine.Installed();
        _machine.Folders.Kinds[TestMachine.DataFolder] = FolderKindEnum.ReparsePoint;

        CommandAppResult result = await _machine.RunAsync(TestMachine.CommandLine(command));

        Assert.Equal(ConsoleExitCodes.DataFolderAdmitsOthers, result.ExitCode);
        Assert.Contains("DataFolderAdmitsOthers (9): ", result.Output);
        Assert.Empty(_machine.Operations);
        Assert.Empty(_machine.Routes);
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("pair")]
    [InlineData("secret")]
    public async Task A_data_folder_whose_access_list_admits_anyone_else_ends_the_command_with_DataFolderAdmitsOthers_naming_them(string command)
    {
        _machine.Installed();
        _machine.Access.Admit("", @"BUILTIN\Users", @"OFFICE\clerk");

        CommandAppResult result = await _machine.RunAsync(TestMachine.CommandLine(command));

        Assert.Equal(ConsoleExitCodes.DataFolderAdmitsOthers, result.ExitCode);
        Assert.Contains("DataFolderAdmitsOthers (9): ", result.Output);
        Assert.Contains(@"BUILTIN\Users", result.Output);
        Assert.Contains(@"OFFICE\clerk", result.Output);
        Assert.Equal(["others \"\""], _machine.Operations);
        Assert.Equal([TestMachine.AgentSid], _machine.AccessListAccounts.Distinct());
        Assert.Empty(_machine.Routes);
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("pair")]
    [InlineData("secret")]
    public async Task A_policy_that_is_absent_ends_the_command_with_PolicyInvalid(string command)
    {
        _machine.Installed();
        _machine.Data.Root.Unseed("policy.json");

        CommandAppResult result = await _machine.RunAsync(TestMachine.CommandLine(command));

        Assert.Equal(ConsoleExitCodes.PolicyInvalid, result.ExitCode);
        Assert.Contains("PolicyInvalid (10): ", result.Output);
        Assert.Contains("policy.json", result.Output);
        Assert.Equal(["others \"\""], _machine.Operations);
        Assert.Empty(_machine.Routes);
    }

    // A policy without a base, which the agent would refuse at its start with the same reason.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("pair")]
    [InlineData("secret")]
    public async Task A_policy_that_is_invalid_ends_the_command_with_PolicyInvalid_and_the_reason_of_the_agent(string command)
    {
        const string Policy = """{ "bases": [] }""";
        _machine.Installed(Policy);
        AgentPolicyException reason = Assert.Throws<AgentPolicyException>(() => AgentPolicyReader.ReadFile(Encoding.UTF8.GetBytes(Policy)));

        CommandAppResult result = await _machine.RunAsync(TestMachine.CommandLine(command));

        Assert.Equal(ConsoleExitCodes.PolicyInvalid, result.ExitCode);
        Assert.Contains("PolicyInvalid (10): ", result.Output);
        Assert.Contains(reason.Message, result.Output);
        Assert.Equal(["others \"\""], _machine.Operations);
        Assert.Empty(_machine.Routes);
    }
}

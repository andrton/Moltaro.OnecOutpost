using System.Text;
using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Folders;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.DataFolder;

// After elevation, pair, secret, check and metadata report NotInstalled when the data folder is absent. Pair and
// secret report DataFolderAdmitsOthers when the folder is a reparse point. The same code reports an access list that
// admits another account, since machine-wide DPAPI lets any local process decrypt the secrets it can read. An absent
// or invalid policy.json ends the command with PolicyInvalid and the agent's reason.
// Each ends before the command's own steps: pair sends nothing and secret reads no password.
public sealed class DataFolderStepTests
{
    private readonly TestMachine _machine = new();

    public DataFolderStepTests()
    {
        _machine.Input = new FakeInput(_machine.Data, "a password\n");
    }

    [Theory]
    [InlineData("pair")]
    [InlineData("secret")]
    [InlineData("check")]
    [InlineData("metadata")]
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
    [Theory]
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

    // pair and secret run after install, so they give the start's sentence for the data folder, word for word, whose
    // remedy removes no audit log.
    [Theory]
    [InlineData("pair")]
    [InlineData("secret")]
    public async Task A_data_folder_whose_access_list_admits_anyone_else_ends_the_command_with_DataFolderAdmitsOthers_naming_them(string command)
    {
        _machine.Installed();
        _machine.Access.Admit("", @"BUILTIN\Users", @"OFFICE\clerk");
        string reason = StartChecks.AccessReason("", new OtherAccounts(_machine.Access.FullPathOf(""), null, [@"BUILTIN\Users", @"OFFICE\clerk"]))!;

        CommandAppResult result = await _machine.RunAsync(TestMachine.CommandLine(command));

        Assert.Equal(ConsoleExitCodes.DataFolderAdmitsOthers, result.ExitCode);
        Assert.Contains($"DataFolderAdmitsOthers (9): {reason}", TestMachine.LinesOf(result));
        Assert.Contains(@"BUILTIN\Users", result.Output);
        Assert.Contains(@"OFFICE\clerk", result.Output);
        Assert.Equal(["others \"\""], _machine.Operations);
        Assert.Equal([TestMachine.AgentSid], _machine.AccessListAccounts.Distinct());
        Assert.Empty(_machine.Routes);
    }

    [Theory]
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
    [Theory]
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

using System.Text;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Console.Tests.Check;

// The line Files of check: policy.json, agent.json and secrets.dat, read as the agent's start
// reads them, by StartChecks, each failing with the reason that the start gives, and secrets.dat with every password
// that the policy needs and that it lacks. A line that needs a file that failed is not checked: no base is checked
// without a policy, the server without agent.json, and nothing that needs a password without secrets.dat.
public sealed class FilesLineTests : IDisposable
{
    private readonly CheckedMachine _checked = new();

    public void Dispose()
    {
        _checked.Dispose();
    }

    [Fact]
    public async Task A_policy_that_is_invalid_fails_its_line_with_the_reason_of_the_agent_and_no_base_is_checked()
    {
        const string Policy = """{ "bases": [] }""";
        _checked.Machine.Data.Root.Seed(AgentFolders.PolicyName, Policy);
        AgentPolicyException reason = Assert.Throws<AgentPolicyException>(() => AgentPolicyReader.ReadFile(Encoding.UTF8.GetBytes(Policy)));

        CheckRun run = await _checked.RunUnansweredAsync();

        run.AssertCheckFailed();
        Assert.Contains(reason.Message, run.Failed(CheckLineEnum.Files, AgentFolders.PolicyName), StringComparison.Ordinal);
        Assert.Empty(run.RowsOf(CheckLineEnum.Connection));
        Assert.Empty(run.RowsOf(CheckLineEnum.WorkerAccount));
        Assert.Empty(run.RowsOf(CheckLineEnum.WorkerStart));
        Assert.Empty(_checked.Machine.Workers.LogOns);
        Assert.Empty(_checked.Machine.Workers.Processes);
        run.AssertNotChecked(CheckLineEnum.Server, AgentFolders.PolicyName);
    }

    // The file check gives the same remedy as the agent's start.
    [Fact]
    public async Task An_agent_json_that_is_absent_fails_its_line_and_the_server_is_not_checked()
    {
        _checked.Machine.Data.Root.Unseed(AgentFolders.PairingName);

        CheckRun run = await _checked.RunUnansweredAsync();

        run.AssertCheckFailed();
        Assert.Equal(
            "agent.json is absent: the agent is not paired; pair it with the console's pair",
            run.Failed(CheckLineEnum.Files, AgentFolders.PairingName));
        run.AssertNotChecked(CheckLineEnum.Server, AgentFolders.PairingName);
        run.Passed(CheckLineEnum.Connection, "warehouse", "outpost-read");
    }

    // pair refuses while agent.json exists, so the remedy says to remove it first.
    [Theory]
    [InlineData("absent", "secrets.dat is absent; to pair the agent anew, remove agent.json, if any, and run the console's pair")]
    [InlineData("not decrypted", "secrets.dat cannot be decrypted on this machine; to pair the agent anew, remove agent.json, if any, and run the console's pair")]
    public async Task A_secrets_dat_that_cannot_be_used_fails_its_line_and_nothing_that_needs_a_password_or_the_key_is_checked(
        string fault,
        string reason)
    {
        if (fault == "absent")
        {
            _checked.Machine.Data.Root.Unseed(AgentFolders.SecretsName);
        }
        else
        {
            _checked.Machine.Protection.Refuses = true;
        }

        CheckRun run = await _checked.RunUnansweredAsync();

        run.AssertCheckFailed();
        Assert.Equal(reason, run.Failed(CheckLineEnum.Files, AgentFolders.SecretsName));
        run.AssertNotChecked(CheckLineEnum.Server, AgentFolders.SecretsName);
        run.AssertNotChecked(CheckLineEnum.WorkerAccount, AgentFolders.SecretsName, "accounting");
        run.AssertNotChecked(CheckLineEnum.Connection, AgentFolders.SecretsName, "warehouse", "outpost-read");
        Assert.Empty(_checked.Machine.Workers.LogOns);
        Assert.Empty(_checked.Machine.Workers.Processes);
    }

    // Every password missing is named, in the policy's order, read, write and worker for each base; the lines that need
    // one are not checked, and those that do not are.
    [Fact]
    public async Task A_secrets_dat_that_lacks_passwords_of_the_policy_fails_its_line_naming_every_one_in_the_order_of_the_policy()
    {
        AgentSecrets secrets = new(
            _checked.AgentKey.ExportPkcs8PrivateKey(),
            new Dictionary<(string Base, OutpostModeEnum Mode), string> { [("accounting", OutpostModeEnum.Read)] = CheckedMachine.ReadPassword },
            new Dictionary<string, string>());
        _checked.Machine.Data.Root.Seed(AgentFolders.SecretsName, SecretsFile.Write(secrets, _checked.Machine.Protection));

        CheckRun run = await _checked.RunConfirmingAnyAsync();

        run.AssertCheckFailed();
        string reasons = string.Join("\n", run.RowsOf(CheckLineEnum.Files)
            .Where(row => row.Check.Contains(AgentFolders.SecretsName, StringComparison.Ordinal))
            .Select(row =>
            {
                Assert.False(row.Passed, row.ToString());

                return row.Reason;
            }));
        string[] missing =
        [
            "secrets.dat holds no password for the write mode of the base accounting; write it with secret accounting write",
            "secrets.dat holds no password for the worker account of the base accounting; write it with secret accounting worker",
            "secrets.dat holds no password for the read mode of the base warehouse; write it with secret warehouse read",
        ];
        int[] positions = [.. missing.Select(sentence => reasons.IndexOf(sentence, StringComparison.Ordinal))];
        Assert.All(positions, position => Assert.True(position >= 0, $"The line of secrets.dat names not every missing password:\n{reasons}"));
        Assert.Equal(positions.Order(), positions);

        Assert.StartsWith("not checked: ", run.Failed(CheckLineEnum.Connection, "accounting", "outpost-write"), StringComparison.Ordinal);
        Assert.StartsWith("not checked: ", run.Failed(CheckLineEnum.WorkerAccount, "accounting"), StringComparison.Ordinal);
        Assert.StartsWith("not checked: ", run.Failed(CheckLineEnum.Connection, "warehouse", "outpost-read"), StringComparison.Ordinal);
        Assert.Empty(_checked.Machine.Workers.LogOns);
        Assert.DoesNotContain(_checked.Machine.Workers.Processes, process => process.Base == "warehouse");
    }
}

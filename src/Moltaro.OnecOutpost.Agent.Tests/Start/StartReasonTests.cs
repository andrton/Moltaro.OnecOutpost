using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport.FakeWindows;

namespace Moltaro.OnecOutpost.Agent.Tests.Start;

// The reasons of the steps of a start that the agent's start and the console's check share, each with a remedy that
// exists, through StartChecks directly: an entry
// of the data folder whose owner is somebody else, named first, with the icacls command that makes Administrators its
// owner, and one that admits somebody else, with what to do for the folder, its two folders and a file, and for
// secrets.dat what its readers could do; the writers of the program folder; agent.json and secrets.dat absent or not
// decrypted, with the console's pair, which refuses while agent.json exists; and each password missing, with the
// secret command that writes it. The audit log keeps these texts in its started entries.
public sealed class StartReasonTests
{
    private const string DataFolder = @"C:\ProgramData\Moltaro.OnecOutpost";

    private const string NobodyElse = "; expected nobody but Administrators, SYSTEM and the agent's account: ";

    [Fact]
    public void An_entry_that_admits_nobody_else_and_has_no_other_owner_has_no_reason()
    {
        Assert.Null(StartChecks.AccessReason("policy.json", new OtherAccounts(FullPathOf("policy.json"), null, [])));
    }

    [Theory]
    [InlineData("", "the data folder", "inspect and correct its access list as install sets it; never remove it, which holds the audit log and the outcomes not yet sent")]
    [InlineData("audit", "audit", "inspect and correct its access list as install sets it; never remove it, which holds the audit log")]
    [InlineData("outcomes", "outcomes", "inspect and correct its access list as install sets it; never remove it, which holds the outcomes not yet sent")]
    [InlineData("policy.json", "policy.json", "inspect it, then reset it to its folder's with icacls \"{path}\" /reset, run as administrator")]
    [InlineData(@"audit\2026-11-21.jsonl", @"audit\2026-11-21.jsonl", "inspect it, then reset it to its folder's with icacls \"{path}\" /reset, run as administrator")]
    [InlineData("secrets.dat", "secrets.dat", "inspect it, then reset it to its folder's with icacls \"{path}\" /reset, run as administrator; whoever it admitted could decrypt it: pair the agent anew and change the passwords it held")]
    public void An_entry_that_admits_somebody_else_is_named_with_the_accounts_and_the_remedy_for_its_kind(string path, string entry, string remedy)
    {
        OtherAccounts others = new(FullPathOf(path), null, [@"PC-1\intruder", @"BUILTIN\Users"]);

        Assert.Equal(
            $@"the access list of {entry} admits PC-1\intruder, BUILTIN\Users" + NobodyElse + remedy.Replace("{path}", FullPathOf(path), StringComparison.Ordinal),
            StartChecks.AccessReason(path, others));
    }

    // An owner can always change the access list, so Administrators, and nobody else, own every entry.
    [Theory]
    [InlineData("", "the data folder")]
    [InlineData("agent.json", "agent.json")]
    public void An_entry_that_somebody_else_owns_is_named_with_the_command_that_makes_Administrators_its_owner(string path, string entry)
    {
        OtherAccounts others = new(FullPathOf(path), @"PC-1\clerk", []);

        Assert.Equal(
            $"{entry} is owned by PC-1\\clerk, who can change its access list; make Administrators its owner with icacls \"{FullPathOf(path)}\" /setowner *S-1-5-32-544, run as administrator",
            StartChecks.AccessReason(path, others));
    }

    [Fact]
    public void An_entry_that_somebody_else_owns_and_that_admits_others_is_named_for_both_its_owner_first()
    {
        OtherAccounts others = new(FullPathOf("policy.json"), @"PC-1\clerk", [@"BUILTIN\Users"]);

        string reason = StartChecks.AccessReason("policy.json", others)!;

        Assert.StartsWith(
            $"policy.json is owned by PC-1\\clerk, who can change its access list; make Administrators its owner with icacls \"{FullPathOf("policy.json")}\" /setowner *S-1-5-32-544, run as administrator",
            reason,
            StringComparison.Ordinal);
        Assert.EndsWith(
            $@"the access list of policy.json admits BUILTIN\Users" + NobodyElse + $"inspect it, then reset it to its folder's with icacls \"{FullPathOf("policy.json")}\" /reset, run as administrator",
            reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_entry_through_which_somebody_else_may_change_the_program_is_named_with_what_to_do()
    {
        const string Worker = @"C:\Program Files\Moltaro.OnecOutpost\x64\Moltaro.OnecOutpost.ComWorker.exe";

        Assert.Equal(
            $@"the access list of {Worker} lets PC-1\intruder, BUILTIN\Users change it; expected nobody but Administrators, SYSTEM, TrustedInstaller and the agent's account: remove their entries, and make Administrators the owner of what they own",
            StartChecks.WritersReason(Worker, [@"PC-1\intruder", @"BUILTIN\Users"]));
    }

    [Fact]
    public void An_agent_json_that_is_absent_says_that_the_agent_is_not_paired_and_to_pair_it()
    {
        PairedFolder folder = PairedFolder.Create();
        folder.Data.Root.Unseed("agent.json");

        AgentStopException exception = Assert.Throws<AgentStopException>(() =>
            StartChecks.ReadPairing(folder.Data.Root, folder.Data.Root.Names()));

        Assert.Equal("agent.json is absent: the agent is not paired; pair it with the console's pair", exception.Message);
    }

    // pair refuses while agent.json exists, so the remedy removes it first.
    [Theory]
    [InlineData("absent", "secrets.dat is absent; to pair the agent anew, remove agent.json, if any, and run the console's pair")]
    [InlineData("not decrypted", "secrets.dat cannot be decrypted on this machine; to pair the agent anew, remove agent.json, if any, and run the console's pair")]
    public void A_secrets_dat_that_cannot_be_used_says_to_remove_agent_json_and_pair_anew(string fault, string reason)
    {
        PairedFolder folder = PairedFolder.Create();
        FakeProtection protection = folder.Protection;

        if (fault == "absent")
        {
            folder.Data.Root.Unseed("secrets.dat");
        }
        else
        {
            protection.Refuses = true;
        }

        AgentStopException exception = Assert.Throws<AgentStopException>(() =>
            StartChecks.ReadSecrets(folder.Data.Root, folder.Data.Root.Names(), protection));

        Assert.Equal(reason, exception.Message);
    }

    [Fact]
    public void Each_password_missing_is_named_with_the_secret_command_that_writes_it_in_the_order_of_the_policy()
    {
        AgentPolicy policy = AgentPolicyReader.ReadFile("""
            {
              "bases": [
                { "key": "accounting", "file": "D:\\1C\\Accounting", "read": { "account": "r" }, "write": { "account": "w", "safeMode": false, "workerAccount": "outpost-worker" } },
                { "key": "warehouse", "file": "D:\\1C\\Warehouse", "read": { "account": "r" } }
              ]
            }
            """u8.ToArray());
        AgentSecrets secrets = new([], new Dictionary<(string Base, OutpostModeEnum Mode), string>(), new Dictionary<string, string>());

        Assert.Equal(
            [
                "secrets.dat holds no password for the read mode of the base accounting; write it with secret accounting read",
                "secrets.dat holds no password for the write mode of the base accounting; write it with secret accounting write",
                "secrets.dat holds no password for the worker account of the base accounting; write it with secret accounting worker",
                "secrets.dat holds no password for the read mode of the base warehouse; write it with secret warehouse read",
            ],
            StartChecks.MissingPasswords(policy, secrets));
    }

    // The start, pair, secret, check and metadata all report an invalid policy.json with the reason given by the
    // policy reader.
    [Fact]
    public void A_policy_that_is_not_valid_is_named_with_the_reason_of_its_reader()
    {
        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(() => AgentPolicyReader.ReadFile("""{ "bases": [] }"""u8.ToArray()));

        Assert.Equal($"policy.json is not valid: {exception.Message}", StartChecks.PolicyReason(exception));
    }

    // The entry at a path of the data folder as every reason names it: "" is the data folder itself.
    [Theory]
    [InlineData("", "the data folder")]
    [InlineData("audit", "audit")]
    [InlineData(@"outcomes\0b7e6a4c-2f1d-4e3a-9c8b-7a6f5e4d3c2b.json", @"outcomes\0b7e6a4c-2f1d-4e3a-9c8b-7a6f5e4d3c2b.json")]
    public void An_entry_of_the_data_folder_is_named_by_its_path_and_the_folder_itself_as_the_data_folder(string path, string entry)
    {
        Assert.Equal(entry, StartChecks.EntryOf(path));
    }

    private static string FullPathOf(string path)
    {
        return path.Length == 0 ? DataFolder : $@"{DataFolder}\{path}";
    }
}

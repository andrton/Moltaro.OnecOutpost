namespace Moltaro.OnecOutpost.Agent.Workers;

// The rights that the agent and its worker accounts need, by the names LSA gives them, written once for the launcher,
// which requires the agent's two privileges, and for the console, which grants and revokes them.
internal static class AccountRightNames
{
    // "Replace a process level token", for the agent's account.
    public const string AssignPrimaryToken = "SeAssignPrimaryTokenPrivilege";

    // "Adjust memory quotas for a process", for the agent's account.
    public const string IncreaseQuota = "SeIncreaseQuotaPrivilege";

    // "Log on as a batch job", for a worker account.
    public const string BatchLogon = "SeBatchLogonRight";

    // The two privileges with which CreateProcessAsUserW starts a worker under a token that is not the agent's own.
    public static readonly IReadOnlyList<string> AgentPrivileges = [AssignPrimaryToken, IncreaseQuota];
}

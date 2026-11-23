namespace Moltaro.OnecOutpost.Console.Rights;

// The rights that the console grants, by the names LSA gives them.
internal static class AccountRightNames
{
    // "Replace a process level token", for the agent's account.
    public const string AssignPrimaryToken = "SeAssignPrimaryTokenPrivilege";

    // "Adjust memory quotas for a process", for the agent's account.
    public const string IncreaseQuota = "SeIncreaseQuotaPrivilege";

    // "Log on as a batch job", for a worker account.
    public const string BatchLogon = "SeBatchLogonRight";
}

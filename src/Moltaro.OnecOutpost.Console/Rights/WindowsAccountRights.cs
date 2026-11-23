using System.Runtime.Versioning;

namespace Moltaro.OnecOutpost.Console.Rights;

// The rights of accounts through LSA: LsaOpenPolicy with POLICY_LOOKUP_NAMES | POLICY_CREATE_ACCOUNT, no more;
// LsaAddAccountRights, which ignores a right held; LsaRemoveAccountRights without AllRights, an account unknown to LSA
// no error; NTSTATUS through LsaNtStatusToWinError.
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsAccountRights : IAccountRights
{
    public string? UserSid(string account)
    {
        throw new NotSupportedException();
    }

    public void Grant(string sid, IReadOnlyList<string> rights)
    {
        throw new NotSupportedException();
    }

    public void Revoke(string sid, IReadOnlyList<string> rights)
    {
        throw new NotSupportedException();
    }

    // The rights the account holds, by LsaEnumerateAccountRights, for the tests and S10b's check.
    public IReadOnlyList<string> Held(string sid)
    {
        throw new NotSupportedException();
    }
}

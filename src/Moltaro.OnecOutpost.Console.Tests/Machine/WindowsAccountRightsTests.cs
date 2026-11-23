using System.Runtime.Versioning;
using Moltaro.OnecOutpost.Console.Rights;

namespace Moltaro.OnecOutpost.Console.Tests.Machine;

// The rights of accounts through LSA (the S10 brief, "WindowsAccountRights"), with Requires=Windows and
// Requires=Administrator, on the virtual account of a service of the test's own, which stands for the agent's and for a
// worker account, so that no local user is made: Grant adds rights, a right held changing nothing; Revoke takes the
// rights it names and no other, an account that LSA does not know being no error; Held reads them back, none once all
// are taken.
[Trait("Requires", "Windows")]
[Trait("Requires", "Administrator")]
[SupportedOSPlatform("windows")]
public sealed class WindowsAccountRightsTests
{
    private readonly WindowsAccountRights _rights = new();

    [Fact]
    public void Grant_and_Revoke_change_the_rights_that_Held_reads_and_neither_fails_when_it_changes_nothing()
    {
        TestService service = new();
        string[] every = [AccountRightNames.AssignPrimaryToken, AccountRightNames.IncreaseQuota, AccountRightNames.BatchLogon];

        try
        {
            _rights.Revoke(service.Sid, every);
            _rights.Grant(service.Sid, every);
            _rights.Grant(service.Sid, every);

            Assert.Equal(every.Order(StringComparer.Ordinal), _rights.Held(service.Sid).Order(StringComparer.Ordinal));

            _rights.Revoke(service.Sid, [AccountRightNames.AssignPrimaryToken, AccountRightNames.IncreaseQuota]);

            Assert.Equal([AccountRightNames.BatchLogon], _rights.Held(service.Sid));

            _rights.Revoke(service.Sid, [AccountRightNames.BatchLogon]);

            Assert.Empty(_rights.Held(service.Sid));
        }
        finally
        {
            service.Remove();
        }
    }
}

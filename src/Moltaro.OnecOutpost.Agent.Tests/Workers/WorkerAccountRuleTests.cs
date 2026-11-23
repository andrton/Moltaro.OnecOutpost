using Moltaro.OnecOutpost.Agent.Workers;

namespace Moltaro.OnecOutpost.Agent.Tests.Workers;

// Whether a worker account is an ordinary local user (the S9c brief, "WorkerAccountRule.IsOrdinary"), judged by the
// groups of its token, whatever their attributes, and its privileges, enabled or not: true only when no group is
// Administrators, S-1-5-32-544, and each privilege is one that Windows gives Users. Another spelling of a SID counts
// as that SID, and another spelling of a privilege as a privilege beyond Users', so that a doubt never makes an
// account ordinary.
public sealed class WorkerAccountRuleTests
{
    // The groups of a local user's token: Everyone, Users, INTERACTIVE, Authenticated Users, This Organization, Local
    // account, LOCAL, and a group of the machine.
    private static readonly string[] UserGroups =
    [
        "S-1-1-0",
        "S-1-5-32-545",
        "S-1-5-4",
        "S-1-5-11",
        "S-1-5-15",
        "S-1-5-113",
        "S-1-2-0",
        "S-1-5-21-1004336348-1177238915-682003330-1003",
    ];

    // The privileges that Windows gives Users, all of them.
    private static readonly string[] UsersPrivileges =
    [
        "SeChangeNotifyPrivilege",
        "SeShutdownPrivilege",
        "SeUndockPrivilege",
        "SeIncreaseWorkingSetPrivilege",
        "SeTimeZonePrivilege",
    ];

    [Fact]
    public void A_user_outside_Administrators_with_every_privilege_of_Users_is_ordinary()
    {
        Assert.True(WorkerAccountRule.IsOrdinary(UserGroups, UsersPrivileges));
    }

    [Fact]
    public void A_user_outside_Administrators_with_no_privilege_is_ordinary()
    {
        Assert.True(WorkerAccountRule.IsOrdinary(UserGroups, []));
    }

    // Whatever its attributes, so also when the token holds the group for deny only, as a filtered token does.
    [Theory]
    [InlineData("S-1-5-32-544")]
    [InlineData("s-1-5-32-544")]
    public void A_user_in_Administrators_is_not_ordinary(string administrators)
    {
        Assert.False(WorkerAccountRule.IsOrdinary([.. UserGroups, administrators], UsersPrivileges));
    }

    // Privileges that an administrator, a service or a backup operator holds, the two that the agent itself needs, and
    // one of Users' in another spelling.
    [Theory]
    [InlineData("SeImpersonatePrivilege")]
    [InlineData("SeBackupPrivilege")]
    [InlineData("SeRestorePrivilege")]
    [InlineData("SeDebugPrivilege")]
    [InlineData("SeTakeOwnershipPrivilege")]
    [InlineData("SeTcbPrivilege")]
    [InlineData("SeAssignPrimaryTokenPrivilege")]
    [InlineData("SeIncreaseQuotaPrivilege")]
    [InlineData("sechangenotifyprivilege")]
    public void A_user_with_a_privilege_beyond_those_of_Users_is_not_ordinary(string privilege)
    {
        Assert.False(WorkerAccountRule.IsOrdinary(UserGroups, [.. UsersPrivileges, privilege]));
    }
}

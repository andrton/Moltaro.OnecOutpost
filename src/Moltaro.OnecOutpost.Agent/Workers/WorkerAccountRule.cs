namespace Moltaro.OnecOutpost.Agent.Workers;

// The rule that a worker account is an ordinary local user, apart from the launcher so that it is tested on every
// platform: the launcher gives it the groups of the account's token, whatever their attributes, and its privileges,
// enabled or not.
internal static class WorkerAccountRule
{
    // The SID of Administrators.
    private const string Administrators = "S-1-5-32-544";

    // The privileges that Windows gives Users.
    private static readonly string[] UsersPrivileges =
    [
        "SeChangeNotifyPrivilege",
        "SeShutdownPrivilege",
        "SeUndockPrivilege",
        "SeIncreaseWorkingSetPrivilege",
        "SeTimeZonePrivilege",
    ];

    // True only when no group SID is Administrators and each privilege is one of Users'. A SID is compared ignoring
    // case and a privilege by ordinal, so that another spelling counts as an administrator or as a privilege beyond
    // Users', never the other way.
    public static bool IsOrdinary(IReadOnlyCollection<string> groups, IReadOnlyCollection<string> privileges)
    {
        bool administrator = groups.Contains(Administrators, StringComparer.OrdinalIgnoreCase);
        bool usersPrivileges = privileges.All(privilege => UsersPrivileges.Contains(privilege, StringComparer.Ordinal));

        return !administrator && usersPrivileges;
    }
}

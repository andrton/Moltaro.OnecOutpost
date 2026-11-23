namespace Moltaro.OnecOutpost.Console.Rights;

// The rights of accounts as LSA keeps them: the boundary that WindowsAccountRights implements and tests replace. Each
// failure is a Win32Exception with the code of Windows. A SID is in its string form, such as S-1-5-80-….
internal interface IAccountRights
{
    // The SID of a local user, such as {machine}\{name}, by LookupAccountNameW; null when Windows names no such account,
    // or names a group or an alias.
    string? UserSid(string account);

    // Grants the rights, by their names in AccountRightNames; a right already held changes nothing.
    void Grant(string sid, IReadOnlyList<string> rights);

    // Revokes the rights; an account that holds none is no error.
    void Revoke(string sid, IReadOnlyList<string> rights);
}

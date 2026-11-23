using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Moltaro.OnecOutpost.Agent.Access;

// The access lists of the data folder as Windows keeps them, read with FileSystemAclExtensions. Each account is named
// as Windows translates its SID, or by the SID; the agent's own account is WindowsIdentity.GetCurrent(), which is
// NT SERVICE\MoltaroOnecOutpost as a service. An administrator's own account, as the owner of a file or in an entry,
// counts as somebody else, so a file that an administrator's account owns stops the agent until Administrators own it.
[SupportedOSPlatform("windows")]
internal sealed class WindowsAccessLists : IAccessLists
{
    private const AccessControlSections OwnerAndAccess = AccessControlSections.Owner | AccessControlSections.Access;

    // The full path of the data folder, against which the paths given to Others are taken.
    private readonly string _dataFolder;

    // The accounts that may hold rights in the data folder: Administrators, SYSTEM and the account of the agent.
    private readonly SecurityIdentifier[] _admitted;

    public WindowsAccessLists(string dataFolder)
    {
        _dataFolder = dataFolder;

        using WindowsIdentity agent = WindowsIdentity.GetCurrent();

        _admitted =
        [
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            agent.User!,
        ];
    }

    public IReadOnlyList<string> Others(string path)
    {
        FileSystemSecurity security = Read(path.Length == 0 ? _dataFolder : Path.Combine(_dataFolder, path));

        // The owner can rewrite the access list, so an owner counts as much as an entry that gives a right.
        List<SecurityIdentifier> accounts = [];

        if (security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner)
        {
            accounts.Add(owner);
        }

        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            // A deny entry gives no right, and an entry that is only inherited by what the folder holds does not apply
            // to the folder itself.
            bool appliesHere = (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0;

            if (rule.AccessControlType == AccessControlType.Allow
                && appliesHere
                && rule.FileSystemRights != 0
                && rule.IdentityReference is SecurityIdentifier account)
            {
                accounts.Add(account);
            }
        }

        return [.. accounts.Distinct().Where(account => !_admitted.Contains(account)).Select(NameOf)];
    }

    // As IAccessLists says. Limitations: an administrator's own account counts as somebody else, as in Others; a volume
    // whose root gives Authenticated Users Modify fails the parents; the connector's folder is judged by the console's
    // check (S10), not here.
    public IReadOnlyList<(string Path, IReadOnlyList<string> Accounts)> Writers(string folder)
    {
        throw new NotSupportedException();
    }

    // The owner and the access entries of a folder or a file. Every failure is an IOException, as the boundary says:
    // the framework reports a file that does not exist with FileNotFoundException, and a list that the agent may not
    // read with UnauthorizedAccessException, which is turned into one.
    private static FileSystemSecurity Read(string fullPath)
    {
        try
        {
            if (Directory.Exists(fullPath))
            {
                return new DirectoryInfo(fullPath).GetAccessControl(OwnerAndAccess);
            }

            return new FileInfo(fullPath).GetAccessControl(OwnerAndAccess);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new IOException(exception.Message, exception);
        }
    }

    // The name that Windows gives the account, or its SID when Windows cannot name it, such as an account of a domain
    // that no longer exists.
    private static string NameOf(SecurityIdentifier account)
    {
        try
        {
            return account.Translate(typeof(NTAccount)).Value;
        }
        catch (IdentityNotMappedException)
        {
            return account.Value;
        }
    }
}

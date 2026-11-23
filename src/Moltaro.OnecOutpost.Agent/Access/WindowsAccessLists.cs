using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Moltaro.OnecOutpost.Agent.Access;

// The access lists of the data folder and of the program folder as Windows keeps them, read with
// FileSystemAclExtensions. Each account is named as Windows translates its SID, or by the SID; the agent's own account
// is WindowsIdentity.GetCurrent() when the lists are made, which is NT SERVICE\MoltaroOnecOutpost as a service. An
// administrator's own account, as the owner of a file or in an entry, counts as somebody else, so a file that an
// administrator's account owns stops the agent until Administrators own it.
[SupportedOSPlatform("windows")]
internal sealed class WindowsAccessLists : IAccessLists
{
    private const AccessControlSections OwnerAndAccess = AccessControlSections.Owner | AccessControlSections.Access;

    // GENERIC_ALL, GENERIC_EXECUTE and GENERIC_READ, which FileSystemRights does not name. Windows maps the generic
    // rights of an entry that applies to the entry itself when it stores it, so they stand only in a list made
    // otherwise.
    private const FileSystemRights GenericAll = (FileSystemRights)0x10000000;

    private const FileSystemRights GenericExecute = (FileSystemRights)0x20000000;

    private const FileSystemRights GenericRead = unchecked((FileSystemRights)0x80000000);

    // The rights on a parent of the program folder through which somebody else could put a folder of his own in the
    // place of the program folder: deleting the parent or what it holds, or changing its list or its owner.
    private const FileSystemRights ParentRights =
        FileSystemRights.Delete
        | FileSystemRights.DeleteSubdirectoriesAndFiles
        | FileSystemRights.ChangePermissions
        | FileSystemRights.TakeOwnership
        | GenericAll;

    // Every right in the program folder but reading and running, each of which could change the code that it holds.
    private const FileSystemRights ProgramRights =
        ~(FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize | GenericRead | GenericExecute);

    // NT SERVICE\TrustedInstaller, whose SID Windows derives from the name of the service.
    private static readonly SecurityIdentifier TrustedInstaller =
        new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");

    // The full path of the data folder, against which the paths given to Others are taken.
    private readonly string _dataFolder;

    // The accounts that may hold rights in the data folder: Administrators, SYSTEM and the account of the agent.
    private readonly SecurityIdentifier[] _admitted;

    // The accounts that may change the program folder: those of the data folder, and TrustedInstaller, which owns what
    // Windows installs, such as the root of the system's volume.
    private readonly SecurityIdentifier[] _admittedPrograms;

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

        _admittedPrograms = [.. _admitted, TrustedInstaller];
    }

    public IReadOnlyList<string> Others(string path)
    {
        string fullPath = path.Length == 0 ? _dataFolder : Path.Combine(_dataFolder, path);
        FileSystemInfo entry = Directory.Exists(fullPath) ? new DirectoryInfo(fullPath) : new FileInfo(fullPath);

        return Accounts(entry, _admitted, rights => rights != 0);
    }

    // As IAccessLists says. Limitations: an administrator's own account counts as somebody else, as in Others; a volume
    // whose root gives Authenticated Users Modify fails the parents; the connector's folder is judged by the console's
    // check (S10), not here.
    public IReadOnlyList<(string Path, IReadOnlyList<string> Accounts)> Writers(string folder)
    {
        DirectoryInfo programs = new(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)));
        List<DirectoryInfo> parents = [];

        for (DirectoryInfo? parent = programs.Parent; parent is not null; parent = parent.Parent)
        {
            parents.Insert(0, parent);
        }

        List<(string Path, IReadOnlyList<string> Accounts)> writers = [];

        foreach (DirectoryInfo parent in parents)
        {
            Judge(parent, ParentRights, writers);
        }

        Judge(programs, ProgramRights, writers);
        JudgeContent(programs, writers);

        return writers;
    }

    // The entries of a folder by ordinal name, each folder among them followed by what it holds. A reparse point, such
    // as a junction, is judged by its own list and not entered, so that nothing outside the program folder is judged as
    // its content.
    private void JudgeContent(DirectoryInfo folder, List<(string Path, IReadOnlyList<string> Accounts)> writers)
    {
        foreach (FileSystemInfo entry in List(folder))
        {
            Judge(entry, ProgramRights, writers);

            if (entry is DirectoryInfo inner && !inner.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                JudgeContent(inner, writers);
            }
        }
    }

    // Adds the entry, by its full path, when an account that may not change the program folder owns it or an allow entry
    // applying to it gives such an account one of the rights.
    private void Judge(FileSystemInfo entry, FileSystemRights rights, List<(string Path, IReadOnlyList<string> Accounts)> writers)
    {
        List<string> accounts = Accounts(entry, _admittedPrograms, given => (given & rights) != 0);

        if (accounts.Count > 0)
        {
            writers.Add((entry.FullName, accounts));
        }
    }

    // The names of the accounts, other than the admitted ones, that own the entry or that an allow entry applying to the
    // entry itself gives rights that judged accepts: the owner first, then the entries in their order, each once.
    private static List<string> Accounts(FileSystemInfo entry, SecurityIdentifier[] admitted, Func<FileSystemRights, bool> judged)
    {
        FileSystemSecurity security = Read(entry);

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
                && judged(rule.FileSystemRights)
                && rule.IdentityReference is SecurityIdentifier account)
            {
                accounts.Add(account);
            }
        }

        return [.. accounts.Distinct().Where(account => !admitted.Contains(account)).Select(NameOf)];
    }

    // The owner and the access entries of a folder or a file; those of a reparse point are its own, never its
    // target's. Every failure is an IOException, as the boundary says: the framework reports an entry that does not
    // exist with FileNotFoundException or DirectoryNotFoundException, and a list that the agent may not read with
    // UnauthorizedAccessException, which is turned into one.
    private static FileSystemSecurity Read(FileSystemInfo entry)
    {
        try
        {
            if (entry is DirectoryInfo folder)
            {
                return folder.GetAccessControl(OwnerAndAccess);
            }

            return ((FileInfo)entry).GetAccessControl(OwnerAndAccess);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new IOException(exception.Message, exception);
        }
    }

    // The entries of a folder by ordinal name; a folder that cannot be listed is an IOException.
    private static List<FileSystemInfo> List(DirectoryInfo folder)
    {
        try
        {
            return [.. folder.EnumerateFileSystemInfos().OrderBy(entry => entry.Name, StringComparer.Ordinal)];
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

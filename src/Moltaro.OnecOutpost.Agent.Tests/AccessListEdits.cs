using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Moltaro.OnecOutpost.Agent.Tests;

// Changes of the access lists of a test's own temporary folder, for the tests that read them on Windows. The account
// that runs the tests stands for the agent's account, as WindowsIdentity.GetCurrent() is the agent's own.
[SupportedOSPlatform("windows")]
internal static class AccessListEdits
{
    public static SecurityIdentifier Everyone { get; } = new(WellKnownSidType.WorldSid, null);

    // Sets the access list of a folder as the console's install sets that of the data folder: inheritance off, and
    // full control, inherited by everything created inside, for Administrators, SYSTEM and the account that runs the
    // tests only.
    public static void AdmitNobodyElse(string folder)
    {
        DirectorySecurity security = new();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        IdentityReference[] accounts =
        [
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            WindowsIdentity.GetCurrent().User!,
        ];

        foreach (IdentityReference account in accounts)
        {
            security.AddAccessRule(new FileSystemAccessRule(
                account,
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        new DirectoryInfo(folder).SetAccessControl(security);
    }

    // Adds an entry that allows the account to read the data of the file or the folder at the path.
    public static void Allow(string path, IdentityReference account)
    {
        Add(path, new FileSystemAccessRule(account, FileSystemRights.ReadData, AccessControlType.Allow));
    }

    // Adds an entry that denies the account the right to run the file at the path, which takes nobody's access away.
    public static void Deny(string path, IdentityReference account)
    {
        Add(path, new FileSystemAccessRule(account, FileSystemRights.ExecuteFile, AccessControlType.Deny));
    }

    // Adds to a folder an entry that allows the account to read the data of what is created inside it, and that does
    // not apply to the folder itself.
    public static void AllowInsideOnly(string folder, IdentityReference account)
    {
        Add(folder, new FileSystemAccessRule(
            account,
            FileSystemRights.ReadData,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.InheritOnly,
            AccessControlType.Allow));
    }

    // The name that Windows gives the account, or its SID when Windows cannot name it.
    public static string NameOf(SecurityIdentifier account)
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

    private static void Add(string path, FileSystemAccessRule rule)
    {
        if (Directory.Exists(path))
        {
            DirectoryInfo folder = new(path);
            DirectorySecurity security = folder.GetAccessControl();
            security.AddAccessRule(rule);
            folder.SetAccessControl(security);
        }
        else
        {
            FileInfo file = new(path);
            FileSecurity security = file.GetAccessControl();
            security.AddAccessRule(rule);
            file.SetAccessControl(security);
        }
    }
}

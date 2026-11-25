using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Moltaro.OnecOutpost.TestSupport;

// Changes of the access lists of a test's own temporary folder, for the tests that read them on Windows: those of the
// agent's tests with Requires=Windows and the lab's. The account that runs the tests stands for the agent's account,
// as WindowsIdentity.GetCurrent() is the agent's own, and the agent's access lists of these tests are made for it.
[SupportedOSPlatform("windows")]
internal static class AccessListEdits
{
    public static SecurityIdentifier Everyone { get; } = new(WellKnownSidType.WorldSid, null);

    // The account that runs the tests.
    public static SecurityIdentifier TestAccount { get; } = WindowsIdentity.GetCurrent().User!;

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

    // Sets the access list of a folder as the console's install sets that of the program folder: as
    // AdmitNobodyElse, the account that runs the tests standing for the agent's, and read and run for Users, inherited
    // by everything created inside.
    public static void LetUsersReadAndRun(string folder)
    {
        AdmitNobodyElse(folder);

        Add(folder, new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
    }

    // Adds an entry that allows the account these rights, to read data unless a test gives others, on the file or the
    // folder at the path, and on nothing inside.
    public static void Allow(string path, IdentityReference account, FileSystemRights rights = FileSystemRights.ReadData)
    {
        Add(path, new FileSystemAccessRule(account, rights, AccessControlType.Allow));
    }

    // Adds to a folder an entry that allows the account an access mask that FileSystemRights does not name, such as
    // GENERIC_ALL, 0x10000000. Windows maps the generic rights of an entry that applies to the folder itself to the
    // rights they stand for when it stores the entry, as NTFS does.
    public static void AllowMask(string folder, SecurityIdentifier account, int mask)
    {
        DirectoryInfo directory = new(folder);
        DirectorySecurity security = directory.GetAccessControl(AccessControlSections.Access);
        RawSecurityDescriptor descriptor = new(security.GetSecurityDescriptorSddlForm(AccessControlSections.Access));

        descriptor.DiscretionaryAcl!.InsertAce(
            0,
            new CommonAce(AceFlags.None, AceQualifier.AccessAllowed, mask, account, isCallback: false, opaque: null));

        DirectorySecurity changed = new();
        changed.SetSecurityDescriptorSddlForm(descriptor.GetSddlForm(AccessControlSections.Access), AccessControlSections.Access);
        directory.SetAccessControl(changed);
    }

    // Adds an entry that denies the account the right to run the file at the path, which takes nobody's access away.
    public static void Deny(string path, IdentityReference account)
    {
        Add(path, new FileSystemAccessRule(account, FileSystemRights.ExecuteFile, AccessControlType.Deny));
    }

    // Adds to a folder an entry that allows the account these rights, to read data unless a test gives others, on what
    // is created inside it, and that does not apply to the folder itself.
    public static void AllowInsideOnly(
        string folder,
        IdentityReference account,
        FileSystemRights rights = FileSystemRights.ReadData)
    {
        Add(folder, new FileSystemAccessRule(
            account,
            rights,
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

using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Console.Folders;

namespace Moltaro.OnecOutpost.Console.Tests.Machine;

// The folders as NTFS keeps them (the S10 brief, "WindowsFolderSetup", S9c's draft and Q14), with Requires=Windows and
// Requires=Administrator, on the folders of a service of the test's own: the program folder under %ProgramFiles% gets
// O:BAD:PAI(A;OICI;FA;;;BA)(A;OICI;FA;;;SY)(A;OICI;0x1200a9;;;BU), the content of the source, and every entry
// Administrators as its owner with only inherited entries, so that the agent's rule finds nobody who may change it; the
// data folder gets O:BAD:PAI(A;OICI;FA;;;BA)(A;OICI;FA;;;SY)(A;OICI;FR;;;{agent}), and audit\ and outcomes\ inherit it
// with (A;OICI;0x1301bf;;;{agent}) of their own, so that the agent's rule finds nobody else; a data folder is created
// only where nothing is. Administrators become the owner of a file the console writes. Kind tells a folder from a
// reparse point, which it never follows.
[Trait("Requires", "Windows")]
[Trait("Requires", "Administrator")]
[SupportedOSPlatform("windows")]
public sealed class WindowsFolderSetupTests
{
    // FILE_ALL_ACCESS, FILE_GENERIC_READ, and the modify right of the agent in audit\ and outcomes\.
    private const int FullAccess = 0x1F01FF;

    private const int ReadAccess = 0x120089;

    private const int ModifyAccess = 0x1301BF;

    private static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);

    private readonly WindowsFolderSetup _folders = new();

    [Fact]
    public void The_program_folder_gets_the_content_of_the_source_its_lists_and_Administrators_as_the_owner_of_every_entry()
    {
        TestService service = new();

        try
        {
            WriteArchive(service, ("Moltaro.OnecOutpost.Agent.exe", "agent"), (@"x64\Moltaro.OnecOutpost.ComWorker.exe", "worker"));

            _folders.ApplyProgramFolder(service.ProgramFolder, service.Archive);

            Assert.Equal("O:BAD:PAI(A;OICI;FA;;;BA)(A;OICI;FA;;;SY)(A;OICI;0x1200a9;;;BU)", TestService.SddlOf(service.ProgramFolder));
            Assert.Equal("agent", File.ReadAllText(Path.Combine(service.ProgramFolder, "Moltaro.OnecOutpost.Agent.exe")));
            Assert.Equal("worker", File.ReadAllText(Path.Combine(service.ProgramFolder, "x64", "Moltaro.OnecOutpost.ComWorker.exe")));
            AssertOwnedByAdministratorsAndInherited(service.ProgramFolder);
            Assert.Empty(new WindowsAccessLists(service.DataFolder, new SecurityIdentifier(service.Sid)).Writers(service.ProgramFolder));
        }
        finally
        {
            service.Remove();
        }
    }

    // An update empties the program folder before it copies, so a file that the new archive no longer has goes.
    [Fact]
    public void The_program_folder_applied_with_a_source_holds_only_its_content_and_without_one_keeps_what_it_holds()
    {
        TestService service = new();

        try
        {
            WriteArchive(service, ("old.dll", "old"));
            _folders.ApplyProgramFolder(service.ProgramFolder, service.Archive);
            File.Delete(Path.Combine(service.Archive, "old.dll"));
            WriteArchive(service, ("new.dll", "new"));

            _folders.ApplyProgramFolder(service.ProgramFolder, service.Archive);
            _folders.ApplyProgramFolder(service.ProgramFolder, null);

            Assert.Equal(["new.dll"], Directory.EnumerateFileSystemEntries(service.ProgramFolder).Select(Path.GetFileName));
            Assert.Equal("O:BAD:PAI(A;OICI;FA;;;BA)(A;OICI;FA;;;SY)(A;OICI;0x1200a9;;;BU)", TestService.SddlOf(service.ProgramFolder));
            AssertOwnedByAdministratorsAndInherited(service.ProgramFolder);
        }
        finally
        {
            service.Remove();
        }
    }

    [Fact]
    public void A_data_folder_created_admits_Administrators_SYSTEM_and_the_agent_which_may_modify_only_audit_and_outcomes()
    {
        TestService service = new();

        try
        {
            _folders.ApplyDataFolder(service.DataFolder, service.Sid, create: true);

            AssertDataFolder(service);
        }
        finally
        {
            service.Remove();
        }
    }

    // A folder that a local user made under %ProgramData% before install, which install judged by the agent's rule.
    [Fact]
    public void A_data_folder_that_exists_gets_its_lists_again()
    {
        TestService service = new();

        try
        {
            Directory.CreateDirectory(Path.Combine(service.DataFolder, "audit"));

            _folders.ApplyDataFolder(service.DataFolder, service.Sid, create: false);

            AssertDataFolder(service);
        }
        finally
        {
            service.Remove();
        }
    }

    // 183: only a folder that was judged is ever given the lists.
    [Fact]
    public void A_data_folder_is_not_created_where_one_exists()
    {
        TestService service = new();

        try
        {
            Directory.CreateDirectory(service.DataFolder);

            Assert.ThrowsAny<IOException>(() => _folders.ApplyDataFolder(service.DataFolder, service.Sid, create: true));
        }
        finally
        {
            service.Remove();
        }
    }

    // Q14: a file whose owner is somebody else stops the agent, since its owner can always change its list.
    [Fact]
    public void A_file_the_console_writes_gets_Administrators_as_its_owner()
    {
        TestService service = new();

        try
        {
            _folders.ApplyDataFolder(service.DataFolder, service.Sid, create: true);
            string file = Path.Combine(service.DataFolder, "secrets.dat");
            File.WriteAllBytes(file, [1, 2, 3]);
            FileInfo info = new(file);
            FileSecurity security = info.GetAccessControl(AccessControlSections.Owner);
            security.SetOwner(WindowsIdentity.GetCurrent().User!);
            info.SetAccessControl(security);

            _folders.OwnByAdministrators(file);

            Assert.Equal(TestService.Administrators, new FileInfo(file).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier)));
        }
        finally
        {
            service.Remove();
        }
    }

    // A symbolic link to a folder is a reparse point, as a junction is.
    [Fact]
    public void Kind_tells_a_folder_from_a_reparse_point_and_from_nothing()
    {
        TestService service = new();

        try
        {
            string folder = Directory.CreateDirectory(Path.Combine(service.Archive, "folder")).FullName;
            string link = Path.Combine(service.Archive, "link");
            Directory.CreateSymbolicLink(link, folder);

            Assert.Equal(FolderKindEnum.Folder, _folders.Kind(folder));
            Assert.Equal(FolderKindEnum.ReparsePoint, _folders.Kind(link));
            Assert.Equal(FolderKindEnum.Absent, _folders.Kind(Path.Combine(service.Archive, "absent")));
        }
        finally
        {
            service.Remove();
        }
    }

    // The data folder admits nobody else by the agent's rule, and has the entries of the brief: its own, protected, and
    // those that audit\ and outcomes\ inherit with the agent's modify right of their own, each owned by Administrators.
    private static void AssertDataFolder(TestService service)
    {
        Assert.Equal(
            $"O:BAD:PAI(A;OICI;FA;;;BA)(A;OICI;FA;;;SY)(A;OICI;FR;;;{service.Sid})",
            TestService.SddlOf(service.DataFolder));

        SecurityIdentifier agent = new(service.Sid);
        WindowsAccessLists lists = new(service.DataFolder, agent);

        Assert.Empty(lists.Others(""));

        foreach (string name in new[] { "audit", "outcomes" })
        {
            DirectorySecurity security = new DirectoryInfo(Path.Combine(service.DataFolder, name))
                .GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);

            Assert.Equal(TestService.Administrators, security.GetOwner(typeof(SecurityIdentifier)));
            Assert.False(security.AreAccessRulesProtected);
            Assert.Equal([(agent, ModifyAccess)], Entries(security, inherited: false));
            Assert.Equal(
                [(TestService.Administrators, FullAccess), (System, FullAccess), (agent, ReadAccess)],
                Entries(security, inherited: true));
            Assert.Empty(lists.Others(name));
        }
    }

    // The allow entries, explicit or inherited, each its account and its access mask, every one inherited by what the
    // folder holds.
    private static IEnumerable<(SecurityIdentifier Account, int Access)> Entries(DirectorySecurity security, bool inherited)
    {
        return security
            .GetAccessRules(includeExplicit: !inherited, includeInherited: inherited, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Where(rule => rule.AccessControlType == AccessControlType.Allow
                && rule.InheritanceFlags == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit))
            .Select(rule => ((SecurityIdentifier)rule.IdentityReference, (int)rule.FileSystemRights));
    }

    // Every entry inside the folder is owned by Administrators and has no entry of its own in its list.
    private static void AssertOwnedByAdministratorsAndInherited(string folder)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories))
        {
            FileSystemSecurity security = Directory.Exists(path)
                ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access)
                : new FileInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);

            Assert.Equal(TestService.Administrators, security.GetOwner(typeof(SecurityIdentifier)));
            Assert.Empty(security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>());
        }
    }

    // Writes the files, by their paths in the archive, into the folder of the unpacked archive.
    private static void WriteArchive(TestService service, params (string Path, string Content)[] files)
    {
        foreach ((string path, string content) in files)
        {
            string file = Path.Combine(service.Archive, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content);
        }
    }
}

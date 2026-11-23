using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.Access;

// The program folder as Windows keeps its access lists (the S9c brief, "The program folder"): an account that could
// replace the code that the agent's account runs would get the agent's secrets, so Writers gives, by full path and with
// its accounts named as Others names them, each entry that an account other than Administrators, SYSTEM,
// NT SERVICE\TrustedInstaller and the agent's own owns, or that an allow entry applying to the entry itself gives
// - on each parent, from the volume's root down, Delete, DeleteSubdirectoriesAndFiles, ChangePermissions,
//   TakeOwnership or GenericAll: adding a subfolder or a file plants a name only after one of these;
// - on the folder and everything in it, a folder before its entries by ordinal name, any right beyond ReadAndExecute,
//   Synchronize and the generic rights to read and to run; a reparse point is judged by its own list and not entered.
//
// Each test makes the program folder in its temporary folder, as temporary\outer\inner\programs, with the access list
// that the console's install gives it on outer and on programs: inheritance off, full control for Administrators,
// SYSTEM and the account that runs the tests, which stands for the agent's, and read and run for Users. A test judges
// only what Writers gives from outer down: the parents above it are the machine's, whatever they allow.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class ProgramWritersTests : IDisposable
{
    // GENERIC_ALL, which FileSystemRights does not name.
    private const int GenericAll = 0x10000000;

    private static readonly SecurityIdentifier TrustedInstaller =
        new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");

    private static readonly string Everyone = AccessListEdits.NameOf(AccessListEdits.Everyone);

    private readonly TemporaryFolder _temporary = new();

    private readonly string _outer;

    private readonly string _inner;

    private readonly string _programs;

    public ProgramWritersTests()
    {
        _outer = _temporary.PathOf("outer");
        _inner = Path.Combine(_outer, "inner");
        _programs = Path.Combine(_inner, "programs");

        Directory.CreateDirectory(_outer);
        AccessListEdits.LetUsersReadAndRun(_outer);
        Directory.CreateDirectory(_programs);
        AccessListEdits.LetUsersReadAndRun(_programs);

        WriteFile(@"x64\Moltaro.OnecOutpost.ComWorker.exe");
        WriteFile("Moltaro.OnecOutpost.Agent.dll");
        WriteFile("OutpostExecutor.epf");
    }

    public void Dispose()
    {
        _temporary.Dispose();
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_program_folder_as_the_install_sets_it_that_users_may_read_and_run_lets_nobody_else_change_it()
    {
        Assert.Empty(Below(_outer, Writers()));
    }

    // Each right goes beyond reading and running, on a file two folders below the program folder.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData(FileSystemRights.WriteData)]
    [InlineData(FileSystemRights.AppendData)]
    [InlineData(FileSystemRights.WriteExtendedAttributes)]
    [InlineData(FileSystemRights.WriteAttributes)]
    [InlineData(FileSystemRights.Delete)]
    [InlineData(FileSystemRights.ChangePermissions)]
    [InlineData(FileSystemRights.TakeOwnership)]
    public void A_file_deep_in_the_program_folder_that_gives_somebody_else_a_right_to_change_it_is_reported_with_the_account(
        FileSystemRights rights)
    {
        string file = WriteFile(@"x64\runtimes\worker.dll");
        AccessListEdits.Allow(file, AccessListEdits.Everyone, rights);

        Assert.Equal([$"{file}: {Everyone}"], Below(_outer, Writers()));
    }

    // A parent of the program folder that somebody else may delete, or whose list he may change, lets him put a folder
    // of his own in its place: the parent just above, and one further up.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("inner", FileSystemRights.Delete)]
    [InlineData("outer", FileSystemRights.Delete)]
    [InlineData("outer", FileSystemRights.DeleteSubdirectoriesAndFiles)]
    [InlineData("outer", FileSystemRights.ChangePermissions)]
    [InlineData("outer", FileSystemRights.TakeOwnership)]
    public void A_parent_that_gives_somebody_else_a_right_to_delete_it_or_to_change_its_list_is_reported(
        string parent,
        FileSystemRights rights)
    {
        string path = parent == "inner" ? _inner : _outer;
        AccessListEdits.Allow(path, AccessListEdits.Everyone, rights);

        Assert.Equal([$"{path}: {Everyone}"], Below(_outer, Writers()));
    }

    // Windows maps the generic right of an entry that applies to the folder itself when it stores it, to full control,
    // so the parent is reported whether the right is judged as GenericAll or as what it stands for.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_parent_that_gives_somebody_else_GenericAll_is_reported()
    {
        AccessListEdits.AllowMask(_outer, AccessListEdits.Everyone, GenericAll);

        Assert.Equal([$"{_outer}: {Everyone}"], Below(_outer, Writers()));
    }

    // C:\ lets Authenticated Users add a subfolder; a name planted so replaces nothing of the program folder.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_parent_on_which_somebody_else_may_add_a_folder_or_a_file_but_not_delete_is_not_judged_by_itself()
    {
        AccessListEdits.Allow(_outer, AccessListEdits.Everyone, FileSystemRights.Write | FileSystemRights.ReadAndExecute);

        Assert.Empty(Below(_outer, Writers()));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void Administrators_SYSTEM_TrustedInstaller_and_the_agents_account_may_change_the_program_folder()
    {
        using WindowsIdentity agent = WindowsIdentity.GetCurrent();
        string file = Path.Combine(_programs, "Moltaro.OnecOutpost.Agent.dll");

        foreach (IdentityReference account in new IdentityReference[]
        {
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            TrustedInstaller,
            agent.User!,
        })
        {
            AccessListEdits.Allow(file, account, FileSystemRights.FullControl);
        }

        AccessListEdits.Allow(_outer, TrustedInstaller, FileSystemRights.FullControl);

        Assert.Empty(Below(_outer, Writers()));
    }

    // An entry that applies only to what is created inside a folder gives nothing to the folder itself, and the folder
    // holds nothing it applies to.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void An_entry_that_applies_only_inside_an_empty_folder_gives_nothing()
    {
        string empty = Path.Combine(_programs, "empty");
        Directory.CreateDirectory(empty);
        AccessListEdits.AllowInsideOnly(empty, AccessListEdits.Everyone, FileSystemRights.FullControl);

        Assert.Empty(Below(_outer, Writers()));
    }

    // The junction's own list is the one it inherited from the program folder. Its target, outside the program
    // folder, lets everyone add a file, and holds a file that everyone may change: entering the junction, or judging
    // it by the list of its target, would report one of them.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_junction_in_the_program_folder_is_judged_by_its_own_list_and_not_entered()
    {
        string outside = _temporary.PathOf("outside");
        Directory.CreateDirectory(outside);
        AccessListEdits.Allow(outside, AccessListEdits.Everyone, FileSystemRights.WriteData);
        string planted = Path.Combine(outside, "planted.dll");
        File.WriteAllBytes(planted, [0x4D, 0x5A]);
        AccessListEdits.Allow(planted, AccessListEdits.Everyone, FileSystemRights.FullControl);
        string link = Path.Combine(_programs, "link");

        CreateJunction(link, outside);

        try
        {
            IReadOnlyList<(string Path, IReadOnlyList<string> Accounts)> writers = Writers();

            Assert.Empty(Below(_outer, writers));
            Assert.Empty(Below(outside, writers));
        }
        finally
        {
            // Removes the junction alone, never what it points to.
            Directory.Delete(link);
        }
    }

    // The owner of an entry may change its list, so an owner counts as much as an entry that gives a right. The lists
    // are made while the thread impersonates ANONYMOUS LOGON, which is then the agent's account, so that the account
    // that runs the tests is somebody else; the file gives it no right, and it is the file's owner.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void An_entry_that_an_account_other_than_the_agents_owns_is_reported_with_that_account()
    {
        using WindowsIdentity tests = WindowsIdentity.GetCurrent();
        string owned = WriteFile("owned.dll");
        OwnedWithoutRights(owned, tests.User!);
        WindowsAccessLists access = AnonymousLogon.Run(() => new WindowsAccessLists(_temporary.PathOf("data")));

        try
        {
            Assert.Contains($"{owned}: {AccessListEdits.NameOf(tests.User!)}", Below(_outer, access.Writers(_programs)));
        }
        finally
        {
            // The owner may always change the list, so the folder can be removed after.
            AccessListEdits.Allow(owned, tests.User!, FileSystemRights.FullControl);
        }
    }

    // The parents from the outer one down, then the program folder, a folder before its entries, and the entries of a
    // folder by ordinal name: Z.dll before a.dll, and a.dll before sub.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void The_entries_come_from_the_parents_down_and_in_the_program_folder_a_folder_before_its_entries_by_ordinal_name()
    {
        string upper = WriteFile("Z.dll");
        string lower = WriteFile("a.dll");
        string deep = WriteFile(@"sub\a.dll");
        string sub = Path.Combine(_programs, "sub");

        foreach (string path in new[] { deep, sub, lower, upper })
        {
            AccessListEdits.Allow(path, AccessListEdits.Everyone, FileSystemRights.WriteData);
        }

        AccessListEdits.Allow(_inner, AccessListEdits.Everyone, FileSystemRights.Delete);

        Assert.Equal(
            [$"{_inner}: {Everyone}", $"{upper}: {Everyone}", $"{lower}: {Everyone}", $"{sub}: {Everyone}", $"{deep}: {Everyone}"],
            Below(_outer, Writers()));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void The_lists_of_a_program_folder_that_does_not_exist_cannot_be_read_and_fail_with_IOException()
    {
        WindowsAccessLists access = new(_temporary.PathOf("data"));

        Assert.ThrowsAny<IOException>(() => access.Writers(_temporary.PathOf("missing")));
    }

    private IReadOnlyList<(string Path, IReadOnlyList<string> Accounts)> Writers()
    {
        return new WindowsAccessLists(_temporary.PathOf("data")).Writers(_programs);
    }

    // The entries at the folder and below it, in the order Writers gives them, each as "{path}: {accounts}".
    private static List<string> Below(string folder, IReadOnlyList<(string Path, IReadOnlyList<string> Accounts)> writers)
    {
        return
        [
            .. writers
                .Where(entry => entry.Path.Equals(folder, StringComparison.OrdinalIgnoreCase)
                    || entry.Path.StartsWith(folder + @"\", StringComparison.OrdinalIgnoreCase))
                .Select(entry => $"{entry.Path}: {string.Join(", ", entry.Accounts)}"),
        ];
    }

    // A file of the program folder with a few bytes, its folders created, which inherit its list; its full path.
    private string WriteFile(string relativePath)
    {
        string path = Path.Combine(_programs, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x4D, 0x5A]);

        return path;
    }

    // The file is owned by the account and its list, inheritance off, gives that account no right: full control for
    // Administrators and SYSTEM, read and run for Users. The account that creates a file may make itself its owner.
    private static void OwnedWithoutRights(string file, SecurityIdentifier owner)
    {
        FileSecurity security = new();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute,
            AccessControlType.Allow));

        new FileInfo(file).SetAccessControl(security);
    }

    // A junction at the link to the folder, made by mklink of cmd.exe, which needs no privilege.
    private static void CreateJunction(string link, string folder)
    {
        ProcessStartInfo start = new("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in new[] { "/c", "mklink", "/J", link, folder })
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("cmd.exe did not start.");
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();

        Assert.True(process.WaitForExit(TestTimes.Deadline), "mklink did not end.");
        Assert.True(process.ExitCode == 0, $"mklink failed: {output}");
        Assert.True(new DirectoryInfo(link).Attributes.HasFlag(FileAttributes.ReparsePoint), "The link is not a junction.");
    }
}

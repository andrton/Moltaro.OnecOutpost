using System.Runtime.Versioning;
using System.Security.Principal;
using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Rights;
using Moltaro.OnecOutpost.TestSupport;
using Spectre.Console.Cli.Testing;
using Spectre.Console.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Machine;

// install then uninstall on this machine (the S10 brief, Acceptance, and "Tests with Requires=Windows and
// Requires=Administrator"), through CommandAppTester with ConsoleApp.Configure and the parts that Program makes, the
// Windows implementations, for a service of the test's own and its folders: install registers the service under its
// virtual account with its program in the program folder, grants that account its two privileges, and creates the data
// folder with its access list; uninstall removes the service and the two privileges and leaves both folders.
[Trait("Requires", "Windows")]
[Trait("Requires", "Administrator")]
[SupportedOSPlatform("windows")]
public sealed class InstallOnWindowsTests
{
    [Fact]
    public async Task Install_then_uninstall_register_and_remove_the_service_under_its_virtual_account_and_leave_its_folders()
    {
        await TestService.RunAsync(async service =>
        {
            WindowsAccountRights rights = new();

            Directory.CreateDirectory(service.Archive);
            File.WriteAllText(Path.Combine(service.Archive, "Moltaro.OnecOutpost.Agent.exe"), "a stand-in for the agent");
            ConsoleParts parts =
                Program.MachineParts(service.Name, service.Archive, service.ProgramFolder, service.DataFolder);

            CommandAppResult installed = await RunAsync(parts, "install");

            Assert.Equal(ConsoleExitCodes.Success, installed.ExitCode);
            (uint type, uint start, string binaryPath, string account, _) = service.Configuration();
            Assert.Equal((TestService.OwnProcess, TestService.AutomaticStart), (type, start));
            Assert.Equal($"\"{Path.Combine(service.ProgramFolder, "Moltaro.OnecOutpost.Agent.exe")}\" service", binaryPath);
            Assert.Equal(service.Account, account, ignoreCase: true);
            Assert.True(File.Exists(Path.Combine(service.ProgramFolder, "Moltaro.OnecOutpost.Agent.exe")));
            Assert.Contains(AccountRightNames.AssignPrimaryToken, rights.Held(service.Sid));
            Assert.Contains(AccountRightNames.IncreaseQuota, rights.Held(service.Sid));
            Assert.Equal(
                TestService.Unordered($"O:BAD:PAI(A;OICI;FA;;;BA)(A;OICI;FA;;;SY)(A;OICI;FR;;;{service.Sid})"),
                TestService.ListsOf(service.DataFolder));
            Assert.Empty(new WindowsAccessLists(service.DataFolder, new SecurityIdentifier(service.Sid)).Others("").All);

            CommandAppResult removed = await RunAsync(parts, "uninstall");

            Assert.Equal(ConsoleExitCodes.Success, removed.ExitCode);
            Assert.False(service.Exists());
            Assert.DoesNotContain(AccountRightNames.AssignPrimaryToken, rights.Held(service.Sid));
            Assert.DoesNotContain(AccountRightNames.IncreaseQuota, rights.Held(service.Sid));
            Assert.True(Directory.Exists(service.ProgramFolder));
            Assert.True(Directory.Exists(service.DataFolder));
        });
    }

    private static Task<CommandAppResult> RunAsync(ConsoleParts parts, params string[] arguments)
    {
        CommandAppTester tester = new(console: new TestConsole().Width(int.MaxValue));
        tester.Configure(config => ConsoleApp.Configure(config, parts));

        return TestTimes.EndOf(tester.RunAsync(arguments, TestContext.Current.CancellationToken));
    }
}

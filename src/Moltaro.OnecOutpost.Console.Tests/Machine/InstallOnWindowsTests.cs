using System.Runtime.Versioning;
using System.Security.Principal;
using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Agent.Connector;
using Moltaro.OnecOutpost.Agent.Files;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Connector;
using Moltaro.OnecOutpost.Console.Folders;
using Moltaro.OnecOutpost.Console.Rights;
using Moltaro.OnecOutpost.Console.Service;
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
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Install_then_uninstall_register_and_remove_the_service_under_its_virtual_account_and_leave_its_folders()
    {
        TestService service = new();
        WindowsAccountRights rights = new();

        try
        {
            Directory.CreateDirectory(service.Archive);
            File.WriteAllText(Path.Combine(service.Archive, "Moltaro.OnecOutpost.Agent.exe"), "a stand-in for the agent");
            ConsoleParts parts = PartsOf(service);

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
                $"O:BAD:PAI(A;OICI;FA;;;BA)(A;OICI;FA;;;SY)(A;OICI;FR;;;{service.Sid})",
                TestService.SddlOf(service.DataFolder));
            Assert.Empty(new WindowsAccessLists(service.DataFolder, new SecurityIdentifier(service.Sid)).Others(""));

            CommandAppResult removed = await RunAsync(parts, "uninstall");

            Assert.Equal(ConsoleExitCodes.Success, removed.ExitCode);
            Assert.False(service.Exists());
            Assert.DoesNotContain(AccountRightNames.AssignPrimaryToken, rights.Held(service.Sid));
            Assert.DoesNotContain(AccountRightNames.IncreaseQuota, rights.Held(service.Sid));
            Assert.True(Directory.Exists(service.ProgramFolder));
            Assert.True(Directory.Exists(service.DataFolder));
        }
        finally
        {
            service.Remove();
        }
    }

    // The parts as Program makes them of the machine, with the service, the folders and the source of the test.
    private static ConsoleParts PartsOf(TestService service)
    {
        return new ConsoleParts(
            true,
            service.Name,
            service.Archive,
            service.ProgramFolder,
            service.DataFolder,
            new DiskFolder(service.DataFolder),
            new WindowsServiceControl(TimeProvider.System),
            new WindowsAccountRights(),
            new WindowsFolderSetup(),
            sid => new WindowsAccessLists(service.DataFolder, new SecurityIdentifier(sid)),
            new MachineDpapi(),
            ConnectorFinder.Machine(),
            new RegSvr32Registration(),
            (server, proxy) => ServerRoute.CreateHandler(server, proxy),
            null,
            TimeProvider.System,
            "0.1.0-test");
    }

    private static Task<CommandAppResult> RunAsync(ConsoleParts parts, params string[] arguments)
    {
        CommandAppTester tester = new(console: new TestConsole().Width(int.MaxValue));
        tester.Configure(config => ConsoleApp.Configure(config, parts));

        return TestTimes.EndOf(tester.RunAsync(arguments, TestContext.Current.CancellationToken));
    }
}

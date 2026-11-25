using Moltaro.OnecOutpost.Console.Commands;

namespace Moltaro.OnecOutpost.Console.Tests.Runs;

// The exit codes of the console are part of its contract: a script that runs a command reads its end by them, so each
// exit code keeps its assigned number.
public sealed class ExitCodeTests
{
    [Fact]
    public void Each_exit_code_has_the_number_of_the_table_of_the_brief()
    {
        Assert.Equal(0, ConsoleExitCodes.Success);
        Assert.Equal(1, ConsoleExitCodes.Failure);
        Assert.Equal(2, ConsoleExitCodes.Usage);
        Assert.Equal(3, ConsoleExitCodes.NotElevated);
        Assert.Equal(4, ConsoleExitCodes.NotWindows);
        Assert.Equal(5, ConsoleExitCodes.Cancelled);
        Assert.Equal(6, ConsoleExitCodes.NotInstalled);
        Assert.Equal(7, ConsoleExitCodes.StopTimedOut);
        Assert.Equal(8, ConsoleExitCodes.ProgramFolderWriters);
        Assert.Equal(9, ConsoleExitCodes.DataFolderAdmitsOthers);
        Assert.Equal(10, ConsoleExitCodes.PolicyInvalid);
        Assert.Equal(11, ConsoleExitCodes.AlreadyPaired);
        Assert.Equal(12, ConsoleExitCodes.PairingFailed);
        Assert.Equal(13, ConsoleExitCodes.NotPaired);
        Assert.Equal(14, ConsoleExitCodes.NotInPolicy);
        Assert.Equal(15, ConsoleExitCodes.UnknownAccount);
        Assert.Equal(16, ConsoleExitCodes.NotInteractive);
        Assert.Equal(17, ConsoleExitCodes.ConnectorNotRegistered);
        Assert.Equal(18, ConsoleExitCodes.RegistrationFailed);
        Assert.Equal(19, ConsoleExitCodes.CheckFailed);
        Assert.Equal(20, ConsoleExitCodes.ReportFailed);
    }
}

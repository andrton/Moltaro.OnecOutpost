using System.ComponentModel;
using Moltaro.OnecOutpost.Agent.Workers;

namespace Moltaro.OnecOutpost.Agent.Tests.Workers;

// The reasons about a worker account and the agent's privileges that the agent's start and the console's check share
// (the S10b brief, "One wording, with a remedy that exists"), each with a remedy that exists: the sentence of each code
// of LogonUserW that the brief names, the remedy naming the base's secret command where one helps, and for any other
// code the message of Windows with the code; the token beyond an ordinary user's; and the agent's account without its
// two privileges. On every platform: the table is apart from the launcher.
public sealed class WorkerAccountReasonsTests
{
    [Theory]
    [InlineData(1326, "unknown account or wrong password; check workerAccount in the policy, or write the password again with secret accounting worker")]
    [InlineData(1385, "it lacks \"Log on as a batch job\", or a \"Deny log on as a batch job\" names it; secret accounting worker grants the right")]
    [InlineData(1909, "the account is locked out")]
    [InlineData(1331, "the account is disabled")]
    [InlineData(1793, "the account has expired")]
    [InlineData(1330, "its password has expired; set a new one and write it with secret accounting worker")]
    [InlineData(1907, "its password must be changed; set a new one and write it with secret accounting worker")]
    [InlineData(1327, "an account restriction refuses it, such as a blank password outside the console")]
    [InlineData(1328, "its logon hours do not allow it now")]
    [InlineData(1329, "it may not log on at this computer")]
    public void Each_code_of_a_logon_that_the_brief_names_has_its_sentence(int error, string sentence)
    {
        Assert.Equal(sentence, WorkerAccountReasons.CannotLogOn("accounting", error));
    }

    // 1311, ERROR_NO_LOGON_SERVERS: the message of Windows names the code's meaning alone, never a value of the call.
    [Fact]
    public void Any_other_code_of_a_logon_gives_the_message_of_Windows_and_the_code()
    {
        Assert.Equal($"{new Win32Exception(1311).Message} (Windows error 1311)", WorkerAccountReasons.CannotLogOn("accounting", 1311));
    }

    [Fact]
    public void A_worker_account_beyond_an_ordinary_user_and_an_agent_without_its_privileges_are_told_what_to_do()
    {
        Assert.Equal(
            "is an administrator or holds a privilege beyond an ordinary user's; take it out of Administrators and of every group that grants a privilege, or name another local user in the policy",
            WorkerAccountReasons.NotOrdinary);
        Assert.Equal(
            "lacks \"Replace a process level token\" or \"Adjust memory quotas for a process\"; install grants them, and the service has them from its next start",
            WorkerAccountReasons.AgentLacksPrivileges);
    }
}

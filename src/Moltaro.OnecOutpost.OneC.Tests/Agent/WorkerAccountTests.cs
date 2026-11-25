using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.OneC.Tests.Lab;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.OneC.Tests.Agent;

// The lab verifies that a base whose Writes run without safe mode is served
// through the real agent, and every COM worker of the base runs under the worker account of lab.local.json, inside the
// job of the test, so that a script without safe mode can do only what that account can (D12, principle 9). The
// account never signed in and the agent loads no profile, so its HKCU is the default hive and its TEMP the system's;
// the first test is the probe of that risk on the typical bases. The agent's program folder, under the root of the
// system drive, lets Users read and run what the account runs, and its data folder, in the temporary folder of the
// account that runs the tests, admits nobody but Administrators, SYSTEM and that account, as LabAgent sets them. A
// worker that does not open fails its test with what LabRun.WhyNotOpenedAsync says.
[SupportedOSPlatform("windows")]
public sealed class WorkerAccountTests
{
    // Gives whether the file at the path can be read.
    private const string CanReadTheFile = """
        Try
            Data = New BinaryData(Parameters["path"]);
            Result = True;
        Except
            Result = False;
        EndTry;
        """;

    // Gives the name of the Windows account that the worker runs under.
    private const string WindowsUser = """
        Network = New COMObject("WScript.Network");
        Result = Network.UserName;
        """;

    // The probe of the profile: on each copy of a typical base whose Writes run without safe mode, its latest posted
    // document, posted again by a Write under the worker account, keeps its movements, as RepostingTests compares them.
    [Fact]
    public async Task On_each_typical_base_without_safe_mode_a_document_posted_again_under_the_worker_account_keeps_its_movements()
    {
        LabFile lab = LabFile.Read();
        LabAccount workerAccount = LabAgent.WorkerAccountOf(lab);
        List<(LabTypicalBase Base, string Name)> typicalBases =
        [
            .. lab.TypicalBases
                .Select((typicalBase, index) => (typicalBase, $"typicalBases[{index}]"))
                .Where(entry => !entry.typicalBase.WriteSafeMode),
        ];

        Assert.True(typicalBases.Count > 0, "lab.local.json names no copy of a typical base whose writeSafeMode is false.");

        foreach ((LabTypicalBase typicalBase, string name) in typicalBases)
        {
            await PostTheLatestDocumentAgainAsync(typicalBase, name, workerAccount);
        }
    }

    // The control file lies beside the data folder, so that the account reaches both by the same path, and Users may
    // read it: the script reads files, and only the data folder's list keeps the account out.
    [Fact]
    public async Task A_write_without_safe_mode_cannot_read_the_secrets_of_the_agent_and_reads_a_file_that_its_account_may_read()
    {
        LabFile lab = LabFile.Read();
        using LabAgent agent = LabAgent.WithoutSafeMode(lab.TestBase, LabAgent.WorkerAccountOf(lab));
        string control = agent.WriteFileForUsers("control.txt", "the control file of the test");
        await using LabRun run = agent.Start();
        Assert.True(await run.OpenedAsync(OutpostModeEnum.Write), await run.WhyNotOpenedAsync(OutpostModeEnum.Write));

        OutpostOutcomeMessage secrets = await run.OutcomeAsync(run.Send(OutpostModeEnum.Write, CanReadTheFile, PathParameters(Path.Combine(agent.DataPath, "secrets.dat"))));
        OutpostOutcomeMessage readable = await run.OutcomeAsync(run.Send(OutpostModeEnum.Write, CanReadTheFile, PathParameters(control)));

        AssertSucceeded(secrets, "the read of secrets.dat");
        AssertSucceeded(readable, "the read of the control file");
        Assert.False(secrets.Result!.Value.GetBoolean(), "The worker account read secrets.dat.");
        Assert.True(readable.Result!.Value.GetBoolean(), "The worker account did not read the control file.");
        Assert.Equal(AgentExitCodes.Stopped, await run.StopAsync());
    }

    [Fact]
    public async Task A_write_without_safe_mode_runs_under_the_worker_account()
    {
        LabFile lab = LabFile.Read();
        LabAccount workerAccount = LabAgent.WorkerAccountOf(lab);
        using LabAgent agent = LabAgent.WithoutSafeMode(lab.TestBase, workerAccount);
        await using LabRun run = agent.Start();
        Assert.True(await run.OpenedAsync(OutpostModeEnum.Write), await run.WhyNotOpenedAsync(OutpostModeEnum.Write));

        OutpostOutcomeMessage user = await run.OutcomeAsync(run.Send(OutpostModeEnum.Write, WindowsUser));

        AssertSucceeded(user, "the name of the Windows account");
        Assert.Equal(workerAccount.User, user.Result!.Value.GetString(), ignoreCase: true);
        Assert.Equal(AgentExitCodes.Stopped, await run.StopAsync());
    }

    private static async Task PostTheLatestDocumentAgainAsync(LabTypicalBase typicalBase, string name, LabAccount workerAccount)
    {
        string about = $"{name}, {typicalBase.DocumentType}";
        using LabAgent agent = LabAgent.WithoutSafeMode(typicalBase, workerAccount);
        await using LabRun run = agent.Start();
        Assert.True(
            await run.OpenedAsync(OutpostModeEnum.Read),
            $"{about}: the Read worker did not open under the worker account: {await run.WhyNotOpenedAsync(OutpostModeEnum.Read)}.");
        Assert.True(
            await run.OpenedAsync(OutpostModeEnum.Write),
            $"{about}: the Write worker did not open under the worker account: {await run.WhyNotOpenedAsync(OutpostModeEnum.Write)}.");

        OutpostOutcomeMessage latest = await run.OutcomeAsync(run.Send(
            OutpostModeEnum.Read,
            TypicalBaseScripts.LatestPostedDocument,
            new JsonObject { ["documentType"] = typicalBase.DocumentType }.ToJsonString()));

        AssertSucceededQuietly(latest, $"{about}: the latest posted document");

        string document = new JsonObject
        {
            ["documentType"] = typicalBase.DocumentType,
            ["id"] = latest.Result!.Value.GetString(),
        }.ToJsonString();

        OutpostOutcomeMessage before = await run.OutcomeAsync(run.Send(OutpostModeEnum.Read, TypicalBaseScripts.MovementsOfTheDocument, document));
        OutpostOutcomeMessage posted = await run.OutcomeAsync(run.Send(OutpostModeEnum.Write, TypicalBaseScripts.PostTheDocumentAgain, document));
        OutpostOutcomeMessage after = await run.OutcomeAsync(run.Send(OutpostModeEnum.Read, TypicalBaseScripts.MovementsOfTheDocument, document));

        AssertSucceededQuietly(before, $"{about}: the movements before");
        AssertSucceededQuietly(posted, $"{about}: the posting under the worker account");
        AssertSucceededQuietly(after, $"{about}: the movements after");

        Assert.True(
            before.Result!.Value.EnumerateObject().Any(register => register.Value.GetArrayLength() > 0),
            $"{about}: the latest posted document has no movements to compare.");

        string[] changed = [.. TypicalBaseScripts.Changed(before.Result.Value, after.Result!.Value)];
        Assert.True(changed.Length == 0, $"{about}: the movements changed in {changed.Length} registers.");
        Assert.Equal(AgentExitCodes.Stopped, await run.StopAsync());
    }

    private static string PathParameters(string path)
    {
        return new JsonObject { ["path"] = path }.ToJsonString();
    }

    // The outcome of a script on a copy of a typical base succeeded; otherwise the assertion names its kind and code
    // alone, since 1C's text may name objects of the copy's configuration.
    private static void AssertSucceededQuietly(OutpostOutcomeMessage outcome, string about)
    {
        Assert.True(outcome.Kind == OutpostAgentOutcomeKindEnum.Succeeded, $"{about}: {outcome.Kind} {outcome.Code}");
    }

    // The outcome succeeded; otherwise the assertion says what 1C said.
    private static void AssertSucceeded(OutpostOutcomeMessage outcome, string about)
    {
        Assert.True(
            outcome.Kind == OutpostAgentOutcomeKindEnum.Succeeded,
            $"{about}: {outcome.Kind} {outcome.Code}: {outcome.ErrorText}");
    }
}

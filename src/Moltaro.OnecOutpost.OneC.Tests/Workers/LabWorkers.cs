using Moltaro.OnecOutpost.OneC.Tests.Lab;

namespace Moltaro.OnecOutpost.OneC.Tests.Workers;

// Workers of the lab's bases, started and connected as the agent will start them: a read worker always in safe mode
// under the read account (D12), a write worker under the write account with the safe mode the test gives, or that
// writeSafeMode gives for a typical base.
internal static class LabWorkers
{
    public static Task<WorkerProcess> TestBaseReadAsync()
    {
        LabTestBase testBase = LabFile.Read().TestBase;

        return StartAsync(LabTestBase.Key, "read", testBase.Server, testBase.Database, testBase.Read, safeMode: true);
    }

    public static Task<WorkerProcess> TestBaseWriteAsync(bool safeMode = true)
    {
        LabTestBase testBase = LabFile.Read().TestBase;

        return StartAsync(LabTestBase.Key, "write", testBase.Server, testBase.Database, testBase.Write, safeMode);
    }

    public static Task<WorkerProcess> TypicalBaseReadAsync(LabTypicalBase typicalBase)
    {
        return StartAsync(
            LabTypicalBase.Key,
            "read",
            typicalBase.Server,
            typicalBase.Database,
            typicalBase.Read,
            safeMode: true);
    }

    public static Task<WorkerProcess> TypicalBaseWriteAsync(LabTypicalBase typicalBase)
    {
        return StartAsync(
            LabTypicalBase.Key,
            "write",
            typicalBase.Server,
            typicalBase.Database,
            typicalBase.Write,
            typicalBase.WriteSafeMode);
    }

    // The part of a 1C connection string that names a base on a cluster.
    public static string Location(string server, string database)
    {
        return $"Srvr=\"{server}\";Ref=\"{database}\";";
    }

    private static async Task<WorkerProcess> StartAsync(
        string baseKey,
        string mode,
        string server,
        string database,
        LabAccount account,
        bool safeMode)
    {
        Repository.AssertExecutorBuilt();

        WorkerProcess worker = WorkerProcess.Start([baseKey, mode], includeDiagnostics: baseKey == LabTestBase.Key);

        try
        {
            bool ready = await worker.ConnectAsync(
                Location(server, database),
                account.User,
                account.Password,
                safeMode,
                Repository.ExecutorPath);

            Assert.True(ready, $"No session opened in the base {baseKey} under the account of its {mode} mode. {worker.Diagnostics}");

            return worker;
        }
        catch
        {
            await worker.DisposeAsync();
            throw;
        }
    }
}

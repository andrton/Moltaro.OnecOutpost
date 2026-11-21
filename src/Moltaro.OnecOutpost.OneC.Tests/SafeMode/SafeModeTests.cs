using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.OneC.Tests.Workers;

namespace Moltaro.OnecOutpost.OneC.Tests.SafeMode;

// Safe mode forbids files, the network, COM, external components and starting programs (concept §3, "The two
// commands"). A Read always runs in safe mode; a Write runs without it only where the agent says so (D12).
public sealed class SafeModeTests
{
    private const string ReadTheFirstLine = """
        Reader = New TextReader(Parameters["path"], TextEncoding.UTF8);
        Result = Reader.ReadLine();
        Reader.Close();
        """;

    // The same script reads the file without safe mode, so that the refusal is that of safe mode and nothing else. The
    // file is the test's own, which the account of the test, and so the worker, may read.
    [Fact]
    public async Task Safe_mode_refuses_a_file_operation_that_runs_without_it()
    {
        string line = $"outpost-test {Guid.NewGuid():N}";
        string path = Path.Combine(Path.GetTempPath(), $"outpost-test-{Guid.NewGuid():N}.txt");
        string parameters = new JsonObject { ["path"] = path }.ToJsonString();

        File.WriteAllText(path, line + "\n");

        try
        {
            OutcomeLine inSafeMode;
            OutcomeLine withoutSafeMode;

            await using (WorkerProcess reader = await LabWorkers.TestBaseReadAsync())
            {
                inSafeMode = await reader.RunAsync(ReadTheFirstLine, parameters);
            }

            await using (WorkerProcess writer = await LabWorkers.TestBaseWriteAsync(safeMode: false))
            {
                withoutSafeMode = await writer.RunAsync(ReadTheFirstLine, parameters);
            }

            inSafeMode.AssertIs("failed", "script-error");
            withoutSafeMode.AssertIs("succeeded", null);
            Assert.Equal(line, withoutSafeMode.Result.GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }
}

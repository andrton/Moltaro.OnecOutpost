using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.OneC.Tests.Lab;
using Moltaro.OnecOutpost.OneC.Tests.Workers;

namespace Moltaro.OnecOutpost.OneC.Tests.TypicalBases;

// On each copy of a typical base that lab.local.json names, the latest posted document of its documentType, posted
// again by the tunnel with the safe mode that writeSafeMode gives, has the register movements it had before, which a
// person's posting made (concept §12, O1). The document and its movements come from the base while the test runs; the
// scripts name no object of a typical configuration and read the movements through the register records of the
// document, whatever its registers are. The lab's test of the worker account (the S9c brief) posts again with the same
// scripts through the agent, and compares the movements as this test does.
public sealed class RepostingTests
{
    [Fact]
    public async Task The_latest_posted_document_posted_again_by_the_tunnel_has_the_movements_it_had_before()
    {
        IReadOnlyList<LabTypicalBase> typicalBases = LabFile.Read().TypicalBases;

        Assert.True(typicalBases.Count > 0, "lab.local.json names no copy of a typical base in typicalBases.");

        for (int index = 0; index < typicalBases.Count; index++)
        {
            await PostTheLatestDocumentAgainAsync(typicalBases[index], $"typicalBases[{index}]");
        }
    }

    private static async Task PostTheLatestDocumentAgainAsync(LabTypicalBase typicalBase, string name)
    {
        string about = $"{name}, {typicalBase.Configuration} {typicalBase.Version}, {typicalBase.DocumentType}";

        await using WorkerProcess reader = await LabWorkers.TypicalBaseReadAsync(typicalBase);
        await using WorkerProcess writer = await LabWorkers.TypicalBaseWriteAsync(typicalBase);

        OutcomeLine latest = await reader.RunAsync(
            TypicalBaseScripts.LatestPostedDocument,
            new JsonObject { ["documentType"] = typicalBase.DocumentType }.ToJsonString());

        latest.AssertIs("succeeded", null);

        string document = new JsonObject
        {
            ["documentType"] = typicalBase.DocumentType,
            ["id"] = latest.Result.GetString(),
        }.ToJsonString();

        OutcomeLine before = await reader.RunAsync(TypicalBaseScripts.MovementsOfTheDocument, document);
        OutcomeLine posted = await writer.RunAsync(TypicalBaseScripts.PostTheDocumentAgain, document);
        OutcomeLine after = await reader.RunAsync(TypicalBaseScripts.MovementsOfTheDocument, document);

        before.AssertIs("succeeded", null);
        posted.AssertIs("succeeded", null);
        after.AssertIs("succeeded", null);

        Assert.True(
            before.Result.EnumerateObject().Any(register => register.Value.GetArrayLength() > 0),
            $"{about}: the latest posted document has no movements to compare.");

        string[] changed = [.. TypicalBaseScripts.Changed(before.Result, after.Result)];
        Assert.True(changed.Length == 0, $"{about}: the movements changed in {string.Join(", ", changed)}.");
    }
}

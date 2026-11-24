using System.Text.Json;
using System.Text.Json.Nodes;
using Moltaro.OnecOutpost.OneC.Tests.Lab;
using Moltaro.OnecOutpost.OneC.Tests.Workers;
using Moltaro.OnecOutpost.TestSupport.Guides;

namespace Moltaro.OnecOutpost.OneC.Tests.Guides;

public sealed class ServerGuideTests
{
    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    public async Task A_script_of_the_shell_session_gives_on_the_test_base_the_description_it_was_given(string mode)
    {
        string command = Assert.Single(ShellGuide.Read().Commands, block => block.StartsWith(mode + " ", StringComparison.Ordinal));
        var frame = ShellGuide.ReadFrame(command);
        bool write = mode == "write";
        JsonObject parameters = JsonNode.Parse(frame.Parameters)!.AsObject();
        if (write)
        {
            parameters["description"] = TestBaseScripts.NewDescription();
        }

        string input = parameters.ToJsonString();
        await using WorkerProcess worker = write ? await LabWorkers.TestBaseWriteAsync() : await LabWorkers.TestBaseReadAsync();
        OutcomeLine outcome = await worker.RunAsync(frame.Script, input, frame.CallerKey);

        outcome.AssertIs("succeeded", null);
        using JsonDocument expected = JsonDocument.Parse(input);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, outcome.Result), outcome.ToString());
        if (write)
        {
            await using WorkerProcess reader = await LabWorkers.TestBaseReadAsync();
            OutcomeLine count = await reader.RunAsync(TestBaseScripts.CountProducts, input);
            count.AssertIs("succeeded", null);
            Assert.Equal(1, count.Result.GetInt32());
        }
    }
}

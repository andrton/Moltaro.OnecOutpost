using Moltaro.OnecOutpost.OneC.Tests.Workers;

namespace Moltaro.OnecOutpost.OneC.Tests.Guides;

// Guide G2, "Writing scripts" (the S9b brief, "Guide G2"): nothing in a guide is untried. Every script of
// docs/guides/scripts.md runs on the test base in a worker as LabWorkers starts one, a Read under the read account and
// a Write under the write account with a caller key of its own, and gives the kind and the code its block names,
// whatever the base holds. A bsl block without a mode, and a guide without a script of each mode, fail.
public sealed class ScriptGuideTests
{
    public static TheoryData<string> Scripts => [.. ScriptGuide.Read().Scripts.Select(script => script.Name)];

    [Fact]
    public void The_guide_names_the_mode_of_every_script_and_shows_scripts_of_both_modes()
    {
        ScriptGuide guide = ScriptGuide.Read();

        Assert.Empty(guide.Errors);
        Assert.Contains(guide.Scripts, script => script.Mode == "read");
        Assert.Contains(guide.Scripts, script => script.Mode == "write");
    }

    [Theory]
    [MemberData(nameof(Scripts))]
    public async Task A_script_of_the_guide_gives_on_the_test_base_what_its_block_says(string name)
    {
        GuideScript script = ScriptGuide.Read().Scripts.Single(each => each.Name == name);
        bool write = script.Mode == "write";

        await using WorkerProcess worker = write ? await LabWorkers.TestBaseWriteAsync() : await LabWorkers.TestBaseReadAsync();
        OutcomeLine outcome = await worker.RunAsync(script.Script, script.Parameters, write ? $"guide-{Guid.NewGuid():N}" : null);

        outcome.AssertIs(script.Kind, script.Code);
    }
}

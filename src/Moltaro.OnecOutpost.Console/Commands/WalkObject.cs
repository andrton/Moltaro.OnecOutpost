using System.Text.Json;

namespace Moltaro.OnecOutpost.Console.Commands;

// An object in the metadata report carries the category of its page, the object returned, and any error from the
// probe or the page. A page's storage error applies to objects that
// have no error of their own.
internal sealed record WalkObject(string Category, JsonElement Item, string? Error)
{
    // Tables seen only by the full storage read, appended to the readable page storage.
    public List<JsonElement>? AdditionalStorage { get; set; }
}

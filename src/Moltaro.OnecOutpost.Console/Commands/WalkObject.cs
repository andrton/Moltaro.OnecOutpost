using System.Text.Json;

namespace Moltaro.OnecOutpost.Console.Commands;

// One object of the report of metadata (the S10 brief, "The report"): the category of the page that read it, the object
// as the page gave it, and its error from the probe or the page. A page's storage error applies to objects that
// have no error of their own.
internal sealed record WalkObject(string Category, JsonElement Item, string? Error);

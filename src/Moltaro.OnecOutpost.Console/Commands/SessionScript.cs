using System.Text.Json;

namespace Moltaro.OnecOutpost.Console.Commands;

// One script of check that runs in a worker's session, as a Read without parameters: the line it gives, with its kind
// and subject, its text, and the reader of its result, which throws KeyNotFoundException or InvalidOperationException for
// a result of another form. A session runs its scripts as one ordered list.
internal sealed record SessionScript(
    CheckLineEnum Kind,
    string Subject,
    string Script,
    Func<JsonElement, (bool Passed, string Reason)> Read);

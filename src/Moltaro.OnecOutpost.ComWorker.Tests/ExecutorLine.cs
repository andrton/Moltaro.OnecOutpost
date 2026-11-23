using System.Text;

namespace Moltaro.OnecOutpost.ComWorker.Tests;

// Texts that the fake executor returns, in the form of the executor: {"size":<number>,"outcome":<the outcome object>},
// where size is the length in bytes of the UTF-8 text of the object alone.
internal static class ExecutorLine
{
    private const string SucceededOutcome =
        """{"kind":"succeeded","code":null,"result":1,"errorText":null,"messages":[],"messagesTrimmed":false}""";

    public static string Succeeded => Of(SucceededOutcome);

    public static string Of(string outcome)
    {
        return "{\"size\":" + Encoding.UTF8.GetByteCount(outcome) + ",\"outcome\":" + outcome + "}";
    }
}

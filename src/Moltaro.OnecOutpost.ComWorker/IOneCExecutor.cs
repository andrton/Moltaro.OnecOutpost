namespace Moltaro.OnecOutpost.ComWorker;

// One executor object, the external data processor OutpostExecutor of src/Executor. Worker opens one for each command
// and disposes of it after the command (concept §3: each command gets a fresh executor object).
internal interface IOneCExecutor : IDisposable
{
    // Calls the exported function Run of the executor and returns the text it returned: one line,
    // {"size":<number>,"outcome":<the outcome object of §8 of docs/protocol.md>}, where size is the length in bytes of
    // the UTF-8 text of the outcome object alone, as the executor measured it. The mode is "read" or "write", the
    // parameters are the text of a JSON object as the worker received it, a null caller key is Undefined in 1C, and the
    // outcome limit is a number of bytes. The executor itself never raises, so a call that throws means that the
    // session ended while the script ran.
    string Run(string mode, string script, string parametersJson, string? callerKey, long outcomeLimit);
}

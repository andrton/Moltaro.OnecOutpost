using System.Runtime.Versioning;

namespace Moltaro.OnecOutpost.ComWorker;

// The COM connection of the worker program, through the COM connector installed with 1C. With the session and the
// executor it gives, it is the only code that touches COM (principle 8), late-bound through IDispatch, so that no
// build depends on one installed version of 1C. Each COM object it obtains is released by its owner, once. Not
// implemented yet.
[SupportedOSPlatform("windows")]
internal sealed class ComConnector : IOneCConnector
{
    public IOneCSession Connect(string location, string user, string password)
    {
        throw new NotSupportedException();
    }
}

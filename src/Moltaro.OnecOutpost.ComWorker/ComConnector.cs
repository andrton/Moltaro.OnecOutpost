using System.Runtime.Versioning;

namespace Moltaro.OnecOutpost.ComWorker;

// The COM connection of the worker program, through the COM connector installed with 1C. With the session and the
// executor it gives, it is the only code that touches COM (principle 8), late-bound through IDispatch, so that no
// build depends on one installed version of 1C. Each COM object it obtains is released by its owner, once.
[SupportedOSPlatform("windows")]
internal sealed class ComConnector : IOneCConnector
{
    // The programmatic identifier under which the 1C:Enterprise 8.3 installer registers comcntr.dll.
    private const string ProgId = "V83.COMConnector";

    public IOneCSession Connect(string location, string user, string password)
    {
        // Windows keeps the registrations of the two bitnesses apart, and COM loads only a connector of the bitness of
        // the process (principle 8).
        Type connectorType = Type.GetTypeFromProgID(ProgId) ?? throw new InvalidOperationException(
            $"The COM connector {ProgId} is not registered for {(Environment.Is64BitProcess ? "64-bit" : "32-bit")} processes.");

        object connector = Activator.CreateInstance(connectorType)
            ?? throw new InvalidOperationException($"The COM connector {ProgId} gave no object.");

        try
        {
            object connection = ComDispatch.Invoke(connector, "Connect", ConnectionString(location, user, password))
                ?? throw new InvalidOperationException("The COM connector gave no connection.");

            return new ComSession(connector, connection);
        }
        catch
        {
            ComDispatch.Release(connector);

            throw;
        }
    }

    // The location names the base and ends with its ";"; the user and the password follow it, each quoted as 1C wants,
    // with every quote inside doubled.
    private static string ConnectionString(string location, string user, string password)
    {
        return location + "Usr=" + Quote(user) + ";Pwd=" + Quote(password) + ";";
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

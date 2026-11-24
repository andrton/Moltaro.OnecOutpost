using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Agent.Connector;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Console.Connector;
using Moltaro.OnecOutpost.Console.Folders;
using Moltaro.OnecOutpost.Console.Rights;
using Moltaro.OnecOutpost.Console.Service;
using Moltaro.OnecOutpost.Console.Workers;

namespace Moltaro.OnecOutpost.Console.Commands;

// What the commands work with, as Program makes it of the machine and a test of fakes: whether the console runs
// elevated; the name of the service, MoltaroOnecOutpost, and the SID of the agent's account, NT SERVICE\{name}'s, for
// which the rights and the access lists are judged; the folder the console runs from, the program folder and the data
// folder with its folders on the disk; the boundaries of the service, the rights, the folders, the access lists of the
// data folder for a SID, the protection of the secrets, the finder and the registration of the COM connector and the
// COM workers that check starts; the handler to the server through its proxy; the redirected standard input, null
// when the input is the console; the clock; and the version of the build.
internal sealed record ConsoleParts(
    bool Elevated,
    string ServiceName,
    string AgentSid,
    string Source,
    string ProgramFolder,
    string DataFolder,
    AgentFolders Data,
    IServiceControl Services,
    IAccountRights Rights,
    IFolderSetup Folders,
    Func<string, IAccessLists> AccessLists,
    IMachineProtection Protection,
    IConnectorFinder Connectors,
    IConnectorRegistration Registration,
    IWorkerStarts Workers,
    Func<Uri, Uri?, HttpMessageHandler> Handlers,
    TextReader? Input,
    TimeProvider Clock,
    string Version);

using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Agent.Connector;
using Moltaro.OnecOutpost.Agent.Files;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Console.Connector;
using Moltaro.OnecOutpost.Console.Folders;
using Moltaro.OnecOutpost.Console.Rights;
using Moltaro.OnecOutpost.Console.Service;

namespace Moltaro.OnecOutpost.Console.Commands;

// What the commands work with, as Program makes it of the machine and a test of fakes: whether the console runs
// elevated; the name of the service, MoltaroOnecOutpost; the folder the console runs from, the program folder and the
// data folder with its folder on the disk; the boundaries of the service, the rights, the folders, the access lists of
// the data folder for the agent's SID, the protection of the secrets, the finder and the registration of the COM
// connector; the handler to the server through its proxy; the redirected standard input, null when the input is the
// console; the clock; and the version of the build.
internal sealed record ConsoleParts(
    bool Elevated,
    string ServiceName,
    string Source,
    string ProgramFolder,
    string DataFolder,
    IAgentFolder Data,
    IServiceControl Services,
    IAccountRights Rights,
    IFolderSetup Folders,
    Func<string, IAccessLists> AccessLists,
    IMachineProtection Protection,
    IConnectorFinder Connectors,
    IConnectorRegistration Registration,
    Func<Uri, Uri?, HttpMessageHandler> Handlers,
    TextReader? Input,
    TimeProvider Clock,
    string Version);

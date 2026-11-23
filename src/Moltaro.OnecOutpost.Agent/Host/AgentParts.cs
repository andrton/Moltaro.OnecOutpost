using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Agent.Workers;

namespace Moltaro.OnecOutpost.Agent.Host;

// What the host runs an agent with: the folders of its data folder, the access lists, the protection of the secrets,
// the launcher of its workers, the full path of the program folder, whose access lists the start judges, the path of
// OutpostExecutor.epf, the handler for a server and a proxy, the clock and the
// agent's version. The program gives the machine's; a test gives fakes and a handler that accepts its certificate.
internal sealed record AgentParts(
    AgentFolders Folders,
    IAccessLists Access,
    IMachineProtection Protection,
    IWorkerLauncher Workers,
    string Programs,
    string Executor,
    Func<Uri, Uri?, HttpMessageHandler> Handlers,
    TimeProvider Clock,
    string Version);

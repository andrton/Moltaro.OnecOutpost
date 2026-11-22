using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Moltaro.Signals;

namespace Moltaro.OnecOutpost.TestSupport.Loopback;

// The ISignals that an application of one node provides: an in-process SignalHost, which needs no socket until a
// client connects to it.
internal static class InProcessSignals
{
    public static void Add(IServiceCollection services)
    {
        services.AddSingleton<ISignals>(_ => new SignalHost(new SignalHostOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
            Token = "test-token",
        }));
    }
}

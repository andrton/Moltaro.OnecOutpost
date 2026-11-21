using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moltaro.OnecOutpost.Server.Store;

namespace Moltaro.OnecOutpost.Server.Purge;

// The purge on every node (concept §5): a round when the host starts, then one after every PurgeInterval, each waiting
// on the clock of the options from the end of the round before. A round removes the pairing tokens that expired unused,
// with their pending agents; S5 adds the commands past their retention. Removing is idempotent, so the nodes need not
// agree on who removes.
//
// A round that fails for anything but the stop of the host is logged as one warning, and the next round comes after the
// interval: otherwise a store that is down would stop the host, as BackgroundService does by default.
internal sealed class PurgeService : BackgroundService
{
    private readonly OutpostStore _store;

    private readonly TimeSpan _interval;

    private readonly TimeProvider _timeProvider;

    private readonly ILogger _logger;

    public PurgeService(OutpostStore store, TimeSpan interval, TimeProvider timeProvider, ILogger logger)
    {
        _store = store;
        _interval = interval;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                try
                {
                    await _store.PurgePairingTokensAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(
                        exception,
                        "A purge of the Outpost store failed; the next one comes after the purge interval.");
                }

                await Task.Delay(_interval, _timeProvider, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The service ends with the stopping token, and without an error, so that a host disposed without a stop
            // does not report it as failed.
        }
    }
}

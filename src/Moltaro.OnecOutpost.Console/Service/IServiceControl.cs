namespace Moltaro.OnecOutpost.Console.Service;

// The service controller of Windows: the boundary that WindowsServiceControl implements and tests replace. Each failure
// is a Win32Exception with the code of Windows.
internal interface IServiceControl
{
    // Creates the service under NT SERVICE\{name}, starting automatically, with the failure actions of the console, or
    // reconfigures the service of that name, never deleting it and keeping its failure actions. True when it created
    // the service, false when it reconfigured it.
    bool Install(string name, string displayName, string binaryPath);

    // Stops the service and waits until it is stopped, at most the timeout by the clock of the implementation.
    Task<ServiceStopEnum> StopAsync(string name, TimeSpan timeout, CancellationToken cancellationToken);

    // Deletes the service; one that Windows has marked for deletion already counts as deleted.
    void Delete(string name);
}

using Makaretu.Dns;
using Microsoft.Extensions.Hosting;

namespace LibraryApiServer.Services;

public sealed class MdnsAdvertiserHostedService : IHostedService, IDisposable
{
    private ServiceDiscovery? _sd;
    private ServiceProfile? _profile;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        const string serviceType = "_libraryapi._tcp"; // Android จะ discover เป็น "_libraryapi._tcp."
        const ushort port = 5269;

        var instanceName = $"{Environment.MachineName}-LibraryApi";
        _profile = new ServiceProfile(instanceName, serviceType, port);

        _profile.AddProperty("path", "/api/meta");
        _profile.AddProperty("ver", "1");

        _sd = new ServiceDiscovery();
        _sd.Advertise(_profile);
        _sd.Announce(_profile);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_sd != null && _profile != null) _sd.Unadvertise(_profile);
        }
        catch { }
        return Task.CompletedTask;
    }

    public void Dispose() => _sd?.Dispose();
}
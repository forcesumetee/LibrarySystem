using Makaretu.Dns;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace LibraryApiServer.Services;

public sealed class MdnsAdvertiserHostedService : IHostedService, IDisposable
{
    private ServiceDiscovery? _sd;
    private ServiceProfile? _profile;

    private readonly IConfiguration _config;
    public MdnsAdvertiserHostedService(IConfiguration config) => _config = config;

    // mDNS must advertise the SAME port the server actually listens on, so it derives the
    // port from the "Urls" config (the listen binding). Default 45269 when not configured.
    private const ushort DefaultPort = 45269;

    private static ushort PortFromUrls(string? urls)
    {
        if (!string.IsNullOrWhiteSpace(urls))
        {
            foreach (var part in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Uri.TryCreate(part, UriKind.Absolute, out var u) && u.Port > 0 && u.Port <= ushort.MaxValue)
                    return (ushort)u.Port;
            }
        }
        return DefaultPort;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        const string serviceType = "_libraryapi._tcp"; // Android จะ discover เป็น "_libraryapi._tcp."
        var port = PortFromUrls(_config["Urls"]);

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
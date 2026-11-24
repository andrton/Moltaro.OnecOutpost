using System.Diagnostics.CodeAnalysis;
using System.Net;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Moltaro.OnecOutpost.Shell;

internal sealed class ShellOrigin(Uri address, IPAddress? ipAddress)
{
    private bool IsLocalhost => address.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);

    public bool Https => address.Scheme == Uri.UriSchemeHttps;

    public string AgentUrl(int boundPort, string prefix)
    {
        string host = address.HostNameType == UriHostNameType.IPv6 ? $"[{address.IdnHost}]" : address.IdnHost;
        return $"{address.Scheme}://{host}:{boundPort}{prefix}";
    }

    public void Listen(KestrelServerOptions options, Action<ListenOptions> configure)
    {
        if (ipAddress is not null)
        {
            options.Listen(ipAddress, address.Port, configure);
        }
        else if (IsLocalhost)
        {
            options.ListenLocalhost(address.Port, configure);
        }
        else
        {
            // A DNS origin is an advertised HTTPS name. Its address may belong to a router in front of this machine.
            options.ListenAnyIP(address.Port, configure);
        }
    }

    public static bool TryParse(string text, [NotNullWhen(true)] out ShellOrigin? origin)
    {
        origin = null;

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? address)
            || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps)
            || text.Any(char.IsWhiteSpace))
        {
            return false;
        }

        // Check the original authority and path because Uri normalizes dot segments and can hide empty delimiters.
        int authorityStart = address.Scheme.Length + 3;
        if (!text.AsSpan(address.Scheme.Length).StartsWith("://", StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> authority = text.AsSpan(authorityStart);
        int slash = authority.IndexOf('/');
        if ((slash >= 0 && slash != authority.Length - 1) || authority.ContainsAny("@?#\\"))
        {
            return false;
        }

        IPAddress? ipAddress = null;
        if (address.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            if (!IPAddress.TryParse(address.IdnHost, out ipAddress))
            {
                return false;
            }

            IPAddress concreteAddress = ipAddress.IsIPv4MappedToIPv6 ? ipAddress.MapToIPv4() : ipAddress;
            if (concreteAddress.Equals(IPAddress.Any) || concreteAddress.Equals(IPAddress.IPv6Any))
            {
                return false;
            }
        }
        else if (address.HostNameType != UriHostNameType.Dns)
        {
            return false;
        }

        if (address.Scheme == Uri.UriSchemeHttp)
        {
            if (ipAddress is not null ? !IPAddress.IsLoopback(ipAddress) : address.Host != "localhost" || address.Port == 0)
            {
                return false;
            }
        }
        else if (address.Port == 0)
        {
            return false;
        }

        origin = new ShellOrigin(address, ipAddress);
        return true;
    }
}

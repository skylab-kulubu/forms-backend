using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.HttpOverrides;
using ProxyNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace Skylab.Forms.Api.Extensions;

public static class ForwardedHeadersExtensions
{
    private const string DefaultTrustedProxyRanges = "10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,127.0.0.0/8,::1/128,fc00::/7";

    public static IServiceCollection AddTrustedForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var networks = ParseTrustedProxyRanges(
            Environment.GetEnvironmentVariable("TRUSTED_PROXY_RANGES")
            ?? configuration["ForwardedHeaders:TrustedProxyRanges"]
            ?? DefaultTrustedProxyRanges);

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.ForwardLimit = null;
            options.KnownProxies.Clear();
            options.KnownNetworks.Clear();

            foreach (var network in networks)
                options.KnownNetworks.Add(network);
        });

        return services;
    }

    private static List<ProxyNetwork> ParseTrustedProxyRanges(string value)
    {
        var networks = new List<ProxyNetwork>();

        foreach (var entry in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            networks.Add(ParseNetwork(entry)
                ?? throw new InvalidOperationException($"TRUSTED_PROXY_RANGES contains an invalid entry: '{entry}'."));
        }

        return networks;
    }

    private static ProxyNetwork? ParseNetwork(string entry)
    {
        var separator = entry.IndexOf('/');

        if (separator < 0)
        {
            return IPAddress.TryParse(entry, out var address) && IsCanonical(address, entry)
                ? new ProxyNetwork(address, address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128)
                : null;
        }

        return System.Net.IPNetwork.TryParse(entry, out var network) && IsCanonical(network.BaseAddress, entry[..separator])
            ? new ProxyNetwork(network.BaseAddress, network.PrefixLength)
            : null;
    }

    private static bool IsCanonical(IPAddress address, string text) => address.AddressFamily switch
    {
        AddressFamily.InterNetwork => address.ToString() == text,
        AddressFamily.InterNetworkV6 => address.ScopeId == 0,
        _ => false
    };
}

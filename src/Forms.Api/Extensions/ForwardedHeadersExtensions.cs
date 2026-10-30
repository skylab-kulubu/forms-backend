using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using ProxyNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace Skylab.Forms.Api.Extensions;

public sealed class TrustedProxyOptions
{
    public const string SectionName = "ForwardedHeaders";

    public string TrustedProxyRanges { get; init; } = "";
}

public static class ForwardedHeadersExtensions
{
    public static IServiceCollection AddTrustedForwardedHeaders(this IServiceCollection services)
    {
        services.AddOptions<TrustedProxyOptions>()
            .BindConfiguration(TrustedProxyOptions.SectionName)
            .Validate(
                proxies => TryParse(proxies.TrustedProxyRanges, out _),
                "ForwardedHeaders:TrustedProxyRanges must be comma-separated CIDR ranges or addresses, like 10.0.0.0/8,::1/128.")
            .ValidateOnStart();

        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<TrustedProxyOptions>>((options, proxies) =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.ForwardLimit = null;
            options.KnownProxies.Clear();
            options.KnownNetworks.Clear();

            TryParse(proxies.Value.TrustedProxyRanges, out var networks);
            foreach (var network in networks)
                options.KnownNetworks.Add(network);
        });

        return services;
    }

    private static bool TryParse(string value, out List<ProxyNetwork> networks)
    {
        networks = [];

        foreach (var entry in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (ParseNetwork(entry) is not { } network) return false;
            networks.Add(network);
        }

        return true;
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

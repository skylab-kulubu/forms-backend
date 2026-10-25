namespace Skylab.Forms.Infrastructure.Integrations.Oidc;

internal interface IServiceTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken ct = default);
}

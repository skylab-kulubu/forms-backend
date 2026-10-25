namespace Skylab.Forms.Infrastructure.Integrations.Oidc;

/// <summary>Adapter'ların servis hesabı token'ı; Application servis hesabını bilmez.</summary>
internal interface IServiceTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken ct = default);
}

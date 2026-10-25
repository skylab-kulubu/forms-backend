namespace Skylab.Forms.Infrastructure.Auth;

/// <summary>Adapter'ların servis hesabı token'ı; Application servis hesabını bilmez.</summary>
public interface IServiceTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken ct = default);
}

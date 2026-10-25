namespace Skylab.Forms.Infrastructure.Auth;

public interface IServiceTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken ct = default);
}

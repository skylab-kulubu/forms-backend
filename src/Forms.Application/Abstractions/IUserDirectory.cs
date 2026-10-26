using Skylab.Forms.Application.Contracts.Identity;

namespace Skylab.Forms.Application.Abstractions;

/// <summary>Kişilerin adı, adresi ve fotoğrafı; kimlik kaynağına ulaşılamazsa kişi bulunamamış sayılır.</summary>
public interface IUserDirectory
{
    Task<UserContract?> GetUserAsync(Guid userId, CancellationToken ct = default);
    Task<List<UserContract>> GetUsersAsync(IEnumerable<Guid> userIds, CancellationToken ct = default);

    /// <summary>"Kişi yok" ile "kaynağa ulaşılamadı"yı ayırır; kişiye göre bir şeyi reddeden çağıranlar bunu kullanır.</summary>
    Task<UserLookup> FindUserAsync(Guid userId, CancellationToken ct = default);
}

public enum UserLookupOutcome
{
    Found,
    NotFound,
    Unavailable
}

public sealed record UserLookup(UserLookupOutcome Outcome, UserContract? User = null);

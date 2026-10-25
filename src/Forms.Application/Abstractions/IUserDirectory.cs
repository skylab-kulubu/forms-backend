using Skylab.Forms.Application.Contracts.Identity;

namespace Skylab.Forms.Application.Abstractions;

/// <summary>Kişilerin adı, adresi ve fotoğrafı; kimlik kaynağına ulaşılamazsa kişi bulunamamış sayılır.</summary>
public interface IUserDirectory
{
    Task<UserContract?> GetUserAsync(Guid userId, CancellationToken ct = default);
    Task<List<UserContract>> GetUsersAsync(IEnumerable<Guid> userIds, CancellationToken ct = default);
}

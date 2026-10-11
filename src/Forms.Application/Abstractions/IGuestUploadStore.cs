namespace Skylab.Forms.Application.Abstractions;

public interface IGuestUploadStore
{
    Task<long> CountAsync(string key, TimeSpan window, CancellationToken ct = default);
    Task SaveSessionAsync(GuestUploadSession session, CancellationToken ct = default);
    Task<GuestUploadSession?> FindSessionAsync(string sessionId, CancellationToken ct = default);
    Task SaveFileAsync(string sessionId, GuestUploadedFile file, DateTime expiresAt, CancellationToken ct = default);
    Task<GuestUploadedFile?> FindFileAsync(string sessionId, Guid mediaId, CancellationToken ct = default);
    Task RemoveFilesAsync(string sessionId, IReadOnlyCollection<Guid> mediaIds, CancellationToken ct = default);
}

public sealed record GuestUploadSession(string Id, Guid FormId, DateTime CreatedAt, DateTime ExpiresAt);

public sealed record GuestUploadedFile(Guid MediaId, string QuestionId, string Type, long Size, string Name);

public sealed class GuestUploadStoreUnavailableException(Exception? inner = null)
    : Exception("Misafir yükleme deposuna ulaşılamadı.", inner);

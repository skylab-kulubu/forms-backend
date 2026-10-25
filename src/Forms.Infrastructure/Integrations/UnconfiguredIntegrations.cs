using System.Net;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Domain.Common;

namespace Skylab.Forms.Infrastructure.Integrations;

internal sealed class UnconfiguredUserDirectory : IUserDirectory
{
    public Task<UserContract?> GetUserAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult(userId == DeletedUser.Id ? new UserContract(userId, null, DeletedUser.DisplayName, null) : null);

    public Task<List<UserContract>> GetUsersAsync(IEnumerable<Guid> userIds, CancellationToken ct = default) =>
        Task.FromResult(userIds.Contains(DeletedUser.Id) ? [new UserContract(DeletedUser.Id, null, DeletedUser.DisplayName, null)] : new List<UserContract>());
}

internal sealed class UnconfiguredAnswerFileStore : IAnswerFileStore
{
    public Task<FileUpload> UploadGuestFileAsync(Stream content, string fileName, string? contentType, CancellationToken ct = default) =>
        Task.FromResult(new FileUpload(FileStoreOutcome.Unavailable));

    public Task<FileLookup> FindAsync(Guid fileId, CancellationToken ct = default) =>
        Task.FromResult(new FileLookup(FileStoreOutcome.Unavailable));

    public Task<FileHold> HoldAsync(Guid fileId, FileHolder holder, Guid? onBehalfOf, CancellationToken ct = default) =>
        Task.FromResult(new FileHold(FileStoreOutcome.Unavailable));

    public Task<bool> ReleaseAsync(Guid fileId, Guid holdId, CancellationToken ct = default) => Task.FromResult(false);

    public Task<FileLink> CreateDownloadLinkAsync(Guid fileId, Guid viewerId, CancellationToken ct = default) =>
        Task.FromResult(new FileLink(FileStoreOutcome.Unavailable));
}

internal sealed class UnconfiguredShortLinks : IShortLinkProvider
{
    private static readonly Task<ShortLinkResult> Failed = Task.FromResult(new ShortLinkResult(ShortLinkStatus.Failed));

    public Task<ShortLinkResult> GetAsync(Guid formId, CancellationToken ct = default) => Failed;

    public Task<ShortLinkResult> EnsureAsync(Guid formId, string url, string? label, string alias, Guid? actorId, CancellationToken ct = default) => Failed;

    public Task<ShortLinkResult> RenameAsync(Guid formId, string alias, string suggestion, CancellationToken ct = default) => Failed;

    public Task<AliasAvailability?> CheckAliasAsync(string alias, CancellationToken ct = default) => Task.FromResult<AliasAvailability?>(null);

    public Task<ShortLinkStats?> GetStatsAsync(Guid formId, CancellationToken ct = default) => Task.FromResult<ShortLinkStats?>(null);

    public Task<QrImage?> GetQrAsync(string alias, bool svg, int size, QrTags tags, CancellationToken ct = default) => Task.FromResult<QrImage?>(null);
}

internal sealed class UnconfiguredMail : IEmailSender, IMailAudienceExport
{
    public Task<MailDelivery> SendAsync(EmailMessage message, CancellationToken ct = default) => Task.FromResult(MailDelivery.Refused);

    public Task<MailAudienceExport> ExportAsync(FormMailRecipientsContract audience, CancellationToken ct = default) =>
        Task.FromResult(new MailAudienceExport(MailAudienceOutcome.Unavailable));
}

internal sealed class UnconfiguredHumanVerifier : IHumanVerifier
{
    public bool IsEnabled => false;

    public Task<HumanCheckVerdict> VerifyAsync(string? token, string action, IPAddress? remoteAddress, CancellationToken ct = default) =>
        Task.FromResult(HumanCheckVerdict.Disabled);

    public Task<bool> IsReachableAsync(CancellationToken ct = default) => Task.FromResult(false);
}

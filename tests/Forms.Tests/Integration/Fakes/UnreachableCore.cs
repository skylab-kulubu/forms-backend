using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Application.Contracts.Responses;

namespace Skylab.Forms.Tests.Integration.Fakes;

/// <summary>Dosya deposuna ulaşılamıyormuş gibi davranır; Forms dosya deposu olmadan da çalışmalı.</summary>
public sealed class UnreachableFileStore : IAnswerFileStore
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

/// <summary>Kısa link servisi yokmuş gibi davranır.</summary>
public sealed class UnreachableShortLinks : IShortLinkProvider
{
    private static readonly Task<ShortLinkResult> Failed = Task.FromResult(new ShortLinkResult(ShortLinkStatus.Failed));

    public Task<ShortLinkResult> GetAsync(Guid formId, CancellationToken ct = default) => Failed;

    public Task<ShortLinkResult> EnsureAsync(Guid formId, string url, string? label, string alias, Guid? actorId, CancellationToken ct = default) => Failed;

    public Task<ShortLinkResult> RenameAsync(Guid formId, string alias, string suggestion, CancellationToken ct = default) => Failed;

    public Task<AliasAvailability?> CheckAliasAsync(string alias, CancellationToken ct = default) => Task.FromResult<AliasAvailability?>(null);

    public Task<ShortLinkStats?> GetStatsAsync(Guid formId, CancellationToken ct = default) => Task.FromResult<ShortLinkStats?>(null);

    public Task<QrImage?> GetQrAsync(string alias, bool svg, int size, QrTags tags, CancellationToken ct = default) => Task.FromResult<QrImage?>(null);
}

/// <summary>Mail aracına ulaşılamıyormuş gibi davranır; gönderilecek mailler testte RecordingMailDispatcher'da kalır.</summary>
public sealed class UnreachableMail : IEmailSender, IMailAudienceExport
{
    public Task<bool> SendAsync(EmailMessage message, CancellationToken ct = default) => Task.FromResult(false);

    public Task<MailAudienceExport> ExportAsync(FormMailRecipientsContract audience, CancellationToken ct = default) =>
        Task.FromResult(new MailAudienceExport(MailAudienceOutcome.Unavailable));
}

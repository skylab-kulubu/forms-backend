using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Contracts.Mail;
using Skylab.Forms.Application.Contracts.Responses;

namespace Skylab.Forms.Tests.Integration.Fakes;

/// <summary>Core'a ulaşılamıyormuş gibi davranır; Forms dosya deposu olmadan da çalışmalı.</summary>
public sealed class UnreachableCoreMedia : ICoreMedia
{
    public Task<CoreMediaUpload> UploadGuestAnswerAsync(Stream content, string fileName, string? contentType, CancellationToken ct = default) =>
        Task.FromResult(new CoreMediaUpload(CoreMediaOutcome.Unavailable));

    public Task<CoreMediaRead> GetAsync(Guid mediaId, CancellationToken ct = default) =>
        Task.FromResult(new CoreMediaRead(CoreMediaOutcome.Unavailable));

    public Task<CoreMediaAttach> AttachAsync(Guid mediaId, CoreMediaOwner owner, Guid? onBehalfOf, CancellationToken ct = default) =>
        Task.FromResult(new CoreMediaAttach(CoreMediaOutcome.Unavailable));

    public Task<bool> DetachAsync(Guid mediaId, Guid attachmentId, CancellationToken ct = default) => Task.FromResult(false);

    public Task<CoreMediaLink> CreateLinkAsync(Guid mediaId, Guid onBehalfOf, CancellationToken ct = default) =>
        Task.FromResult(new CoreMediaLink(CoreMediaOutcome.Unavailable));
}

/// <summary>Kısa link servisi yokmuş gibi davranır.</summary>
public sealed class UnreachableCoreShortLinks : ICoreShortLinks
{
    private static readonly Task<CoreLinkResult> Failed = Task.FromResult(new CoreLinkResult(CoreLinkStatus.Failed));

    public Task<CoreLinkResult> GetAsync(Guid formId, CancellationToken ct = default) => Failed;

    public Task<CoreLinkResult> EnsureAsync(Guid formId, string url, string? label, string alias, Guid? actorId, CancellationToken ct = default) => Failed;

    public Task<CoreLinkResult> RenameAsync(Guid formId, string alias, string suggestion, CancellationToken ct = default) => Failed;

    public Task<CoreAliasAvailability?> CheckAliasAsync(string alias, CancellationToken ct = default) => Task.FromResult<CoreAliasAvailability?>(null);

    public Task<CoreLinkStats?> GetStatsAsync(Guid formId, CancellationToken ct = default) => Task.FromResult<CoreLinkStats?>(null);

    public Task<CoreQrImage?> GetQrAsync(string alias, bool svg, int size, CoreQrTags tags, CancellationToken ct = default) => Task.FromResult<CoreQrImage?>(null);
}

/// <summary>Mail aracına ulaşılamıyormuş gibi davranır; gönderilecek mailler testte RecordingMailDispatcher'da kalır.</summary>
public sealed class UnreachableMail : IEmailSender, IMailAudienceExport
{
    public Task<bool> SendAsync(EmailMessage message, CancellationToken ct = default) => Task.FromResult(false);

    public Task<MailAudienceExport> ExportAsync(FormMailRecipientsContract audience, CancellationToken ct = default) =>
        Task.FromResult(new MailAudienceExport(MailAudienceOutcome.Unavailable));
}

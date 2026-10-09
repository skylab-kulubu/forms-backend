using System.Net;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.GuestUploads;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Domain.Entities;

namespace Skylab.Forms.Application.Services.GuestUploads;

public interface IGuestUploadService
{
    GuestUploadsContract? Capability { get; }
    GuestUploadsContract? CapabilityFor(Form form);

    Task<ServiceResult<GuestUploadSessionContract>> StartSessionAsync(Guid formId, string? turnstileToken, IPAddress? client, CancellationToken ct = default);
    Task<ServiceResult<GuestUploadContract>> UploadAsync(Guid formId, string? sessionId, string? questionId, GuestFile file, IPAddress? client, CancellationToken ct = default);
    Task<ServiceResult<GuestUploadContract>> GetStatusAsync(Guid formId, string? sessionId, Guid mediaId, CancellationToken ct = default);

    Task<GuestSubmitGate> CheckSubmitAsync(Form form, ResponseSubmitRequest request, IPAddress? client, CancellationToken ct = default);
    Task<GuestSubmitGate> CheckAccountSubmitAsync(Form form, ResponseSubmitRequest request, Guid userId, CancellationToken ct = default);
    Task<GuestAttachResult> AttachAsync(IReadOnlyList<GuestSubmitFile> files, Guid responseId, CancellationToken ct = default);
    Task DetachAsync(IReadOnlyList<GuestAttachment> attachments);
    Task ConsumeAsync(string sessionId, IReadOnlyList<GuestSubmitFile> files);
}

public sealed record GuestFile(string FileName, string? ContentType, long Length, Stream Content);

public sealed record GuestSubmitFile(string QuestionId, Guid MediaId);

public sealed record GuestSubmitGate(ServiceResult<ResponseSubmitResult>? Rejection, string? SessionId, IReadOnlyList<GuestSubmitFile> Files, bool Verified = true)
{
    public static readonly GuestSubmitGate Pass = new(null, null, []);
    public static readonly GuestSubmitGate PassUnverified = new(null, null, [], Verified: false);
    public static GuestSubmitGate Reject(ServiceResult<ResponseSubmitResult> rejection) => new(rejection, null, []);
}

public sealed record GuestAttachment(Guid MediaId, Guid AttachmentId);

public sealed record GuestAttachResult(ServiceResult<ResponseSubmitResult>? Rejection, IReadOnlyList<GuestAttachment> Attachments);

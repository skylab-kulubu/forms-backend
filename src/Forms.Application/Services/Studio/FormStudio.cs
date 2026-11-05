using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Draft;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Application.Contracts.Studio;
using Skylab.Forms.Application.Services.Forms;
using Skylab.Forms.Application.Services.GuestUploads;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Studio;

namespace Skylab.Forms.Application.Services.Studio;

public sealed record StudioCaller(Guid UserId, bool IsPlatformAdmin);

public interface IFormStudio
{
    Task<ServiceResult<StudioSnapshotContract>> GetAsync(Guid formId, StudioCaller caller, CancellationToken ct = default);
    Task<ServiceResult<StudioSnapshotContract>> StartAsync(StudioStartRequest request, StudioCaller caller, CancellationToken ct = default);
    Task<ServiceResult<StudioAccessContract>> AccessAsync(Guid formId, StudioCaller caller, CancellationToken ct = default);
    Task<ServiceResult<StudioCatchUpContract>> CatchUpAsync(Guid formId, long since, StudioCaller caller, CancellationToken ct = default);
    Task<ServiceResult<StudioEditContract>> EditAsync(Guid formId, StudioEditRequest request, StudioCaller caller, CancellationToken ct = default);
    Task<ServiceResult<FormContract>> PublishAsync(Guid formId, StudioPublishRequest request, StudioCaller caller, CancellationToken ct = default);
}

public sealed class FormStudio : IFormStudio
{
    private readonly IFormRepository _forms;
    private readonly IFormStudioRepository _studio;
    private readonly IFormsUnitOfWork _uow;
    private readonly IFormWorkflowRepository _workflows;
    private readonly IUserDirectory _users;
    private readonly IFormDraftService _drafts;
    private readonly ICacheService _cache;
    private readonly IGuestUploadService _guestUploads;
    private readonly IStudioNotifier _notifier;
    private readonly TimeProvider _time;
    private readonly ILogger<FormStudio> _logger;

    public FormStudio(
        IFormRepository forms,
        IFormStudioRepository studio,
        IFormsUnitOfWork uow,
        IFormWorkflowRepository workflows,
        IUserDirectory users,
        IFormDraftService drafts,
        ICacheService cache,
        IGuestUploadService guestUploads,
        IStudioNotifier notifier,
        TimeProvider time,
        ILogger<FormStudio> logger)
    {
        _forms = forms;
        _studio = studio;
        _uow = uow;
        _workflows = workflows;
        _users = users;
        _drafts = drafts;
        _cache = cache;
        _guestUploads = guestUploads;
        _notifier = notifier;
        _time = time;
        _logger = logger;
    }

    public async Task<ServiceResult<StudioSnapshotContract>> GetAsync(Guid formId, StudioCaller caller, CancellationToken ct = default)
    {
        var form = await _forms.GetWithCollaboratorsAsync(formId, ct);
        if (form is null) return Fail<StudioSnapshotContract>(ServiceStatus.NotFound, "Form bulunamadı.");

        var role = form.RoleOf(caller.UserId);
        if (!FormAccess.Allows(role, caller.IsPlatformAdmin, FormAction.OpenEditor))
            return Fail<StudioSnapshotContract>(ServiceStatus.NotAuthorized, "Bu formu açma yetkiniz yok.");

        var copy = await _studio.FindWorkingCopyAsync(formId, ct);
        var published = FormDocument.Of(form);
        var document = copy?.Document ?? published;

        var legacyDraft = FormAccess.Allows(role, caller.IsPlatformAdmin, FormAction.EditForm)
            ? await LegacyDraftOfAsync(formId, caller.UserId, ct)
            : null;

        return Ok(new StudioSnapshotContract(
            await ContractOfAsync(form, role, ct),
            new StudioWorkingCopyContract(document.ToJson(), copy?.Version ?? 0, !document.SameAs(published), copy?.UpdatedAt, copy?.UpdatedBy),
            published.ToJson(),
            legacyDraft));
    }

    public async Task<ServiceResult<StudioSnapshotContract>> StartAsync(StudioStartRequest request, StudioCaller caller, CancellationToken ct = default)
    {
        if (!FormDocument.TryRead(request.Document, out var document, out var problem))
            return Fail<StudioSnapshotContract>(ServiceStatus.NotAcceptable, problem);

        if (document.ByteSize > StudioLimits.MaxDocumentBytes)
            return Fail<StudioSnapshotContract>(ServiceStatus.NotAcceptable, $"Form en fazla {StudioLimits.MaxDocumentBytes / 1024} KB olabilir.");

        var form = new Form
        {
            Title = document.Publishable().Title,
            Status = FormStatus.Closed,
            PublishedVersion = 0
        };

        form.Collaborators.Add(new FormCollaborator { FormId = form.Id, UserId = caller.UserId, Role = CollaboratorRole.Owner });
        _forms.Add(form);

        var copy = FormWorkingCopy.Begin(form.Id, document, caller.UserId, _time.GetUtcNow().UtcDateTime);
        _studio.Add(copy);

        await _uow.SaveChangesAsync(ct);

        return Ok(new StudioSnapshotContract(
            await ContractOfAsync(form, CollaboratorRole.Owner, ct),
            new StudioWorkingCopyContract(document.ToJson(), copy.Version, !document.SameAs(FormDocument.Of(form)), copy.UpdatedAt, copy.UpdatedBy),
            FormDocument.Of(form).ToJson(),
            null));
    }

    public async Task<ServiceResult<StudioAccessContract>> AccessAsync(Guid formId, StudioCaller caller, CancellationToken ct = default)
    {
        var form = await _forms.GetWithCollaboratorsAsync(formId, ct);
        if (form is null) return Fail<StudioAccessContract>(ServiceStatus.NotFound, "Form bulunamadı.");

        var role = form.RoleOf(caller.UserId);
        if (!FormAccess.Allows(role, caller.IsPlatformAdmin, FormAction.OpenEditor))
            return Fail<StudioAccessContract>(ServiceStatus.NotAuthorized, "Bu formu açma yetkiniz yok.");

        return Ok(new StudioAccessContract(role, FormAccess.Allows(role, caller.IsPlatformAdmin, FormAction.EditForm)));
    }

    public async Task<ServiceResult<StudioCatchUpContract>> CatchUpAsync(Guid formId, long since, StudioCaller caller, CancellationToken ct = default)
    {
        var form = await _forms.GetWithCollaboratorsAsync(formId, ct);
        if (form is null) return Fail<StudioCatchUpContract>(ServiceStatus.NotFound, "Form bulunamadı.");

        if (!form.Allows(caller.UserId, caller.IsPlatformAdmin, FormAction.OpenEditor))
            return Fail<StudioCatchUpContract>(ServiceStatus.NotAuthorized, "Bu formu açma yetkiniz yok.");

        var copy = await _studio.FindWorkingCopyAsync(formId, ct);
        var version = copy?.Version ?? 0;

        if (since == version) return Ok(new StudioCatchUpContract(version, [], false));
        if (since < 0 || since > version) return Ok(new StudioCatchUpContract(version, [], true));

        var edits = await _studio.GetEditsAfterAsync(formId, since, StudioLimits.MaxCatchUpEdits + 1, ct);
        var contiguous = edits.Count is > 0 and <= StudioLimits.MaxCatchUpEdits
            && edits.Select((edit, index) => edit.Version == since + 1 + index).All(matches => matches);

        if (!contiguous) return Ok(new StudioCatchUpContract(version, [], true));

        var dirty = copy is not null && !copy.Document.SameAs(FormDocument.Of(form));

        return Ok(new StudioCatchUpContract(edits[^1].Version, [.. edits.Select(edit => StudioEditContract.Of(edit, dirty))], false));
    }

    public async Task<ServiceResult<StudioEditContract>> EditAsync(Guid formId, StudioEditRequest request, StudioCaller caller, CancellationToken ct = default)
    {
        if (!FormEditOp.TryReadAll(request.Ops, out var ops, out var problem))
            return Fail<StudioEditContract>(ServiceStatus.NotAcceptable, problem);

        var conflicted = false;

        for (var attempt = 0; attempt < StudioLimits.MaxEditAttempts; attempt++)
        {
            var form = await _forms.GetForEditWithCollaboratorsAsync(formId, ct);
            if (form is null) return Fail<StudioEditContract>(ServiceStatus.NotFound, "Form bulunamadı.");

            if (!form.Allows(caller.UserId, caller.IsPlatformAdmin, FormAction.EditForm))
                return Fail<StudioEditContract>(ServiceStatus.NotAuthorized, "Bu formu düzenleme yetkiniz yok.");

            var published = FormDocument.Of(form);
            var copy = await _studio.GetWorkingCopyAsync(formId, ct);

            if (conflicted && await _studio.FindEditAsync(formId, request.BatchId, ct) is { } known)
                return Ok(StudioEditContract.Of(known, copy is not null && !copy.Document.SameAs(published)));

            var fresh = copy is null;
            copy ??= FormWorkingCopy.Of(form);

            var rules = FormEditRules.Concern(ops) ? await RulesForAsync(formId, ct) : FormEditRules.None;
            var result = FormDocumentEditor.Apply(copy.Document, ops, published, rules);

            if (result.Violation is { } violation)
                return Fail<StudioEditContract>(ServiceStatus.NotAcceptable, violation);

            if (result.Applied.Count == 0)
                return Ok(new StudioEditContract(null, request.BatchId, caller.UserId, new JsonArray(), result.Skipped, !copy.Document.SameAs(published)));

            if (fresh) _studio.Add(copy);

            var edit = copy.Append(result.Document!, result.Applied, request.BatchId, caller.UserId, _time.GetUtcNow().UtcDateTime);
            _studio.Add(edit);

            if (form.PublishedVersion == 0 && copy.Document.Publishable().Title is var title && form.Title != title)
                form.Title = title;

            try
            {
                await _uow.SaveChangesAsync(ct);
            }
            catch (StorageConflictException)
            {
                conflicted = true;
                continue;
            }

            return Ok(StudioEditContract.Of(edit, !copy.Document.SameAs(published), result.Skipped));
        }

        return Fail<StudioEditContract>(ServiceStatus.Conflict, "Form şu an çok yoğun düzenleniyor; değişiklik birazdan yeniden gönderilecek.");
    }

    public async Task<ServiceResult<FormContract>> PublishAsync(Guid formId, StudioPublishRequest request, StudioCaller caller, CancellationToken ct = default)
    {
        var form = await _forms.GetForEditWithCollaboratorsAsync(formId, ct);
        if (form is null) return Fail<FormContract>(ServiceStatus.NotFound, "Form bulunamadı.");

        var role = form.RoleOf(caller.UserId);
        if (!FormAccess.Allows(role, caller.IsPlatformAdmin, FormAction.EditForm))
            return Fail<FormContract>(ServiceStatus.NotAuthorized, "Bu formu kaydetme yetkiniz yok.");

        var copy = await _studio.GetWorkingCopyAsync(formId, ct);
        var workingVersion = copy?.Version ?? 0;

        if (workingVersion != request.WorkingVersion)
            return Fail<FormContract>(ServiceStatus.Conflict, "Siz kaydederken formda yeni değişiklikler oldu. Onları gördükten sonra tekrar kaydedin.");

        var document = (copy?.Document ?? FormDocument.Of(form)).Publishable();

        if (form.PublishedVersion > 0 && document.SameAs(FormDocument.Of(form)))
            return Ok(await ContractOfAsync(form, role, ct));

        var publication = await FormPublication.ApplyAsync(form, UpsertOf(document), _workflows, _guestUploads.Capability is not null, ct);
        if (publication.Failure is { } failure) return failure;

        form.PublishedVersion += 1;

        try
        {
            await _uow.SaveChangesAsync(ct);
        }
        catch (StorageConflictException)
        {
            return Fail<FormContract>(ServiceStatus.Conflict, "Form bu sırada başka bir yerden değişti; sayfayı yenileyip tekrar deneyin.");
        }

        await FormPublication.AfterCommitAsync(formId, publication, _drafts, _cache, ct);

        var published = FormDocument.Of(form);
        await _notifier.PublishedAsync(
            new StudioPublishedContract(formId, form.PublishedVersion, workingVersion, caller.UserId, published.ToJson(), copy is not null && !copy.Document.SameAs(published)),
            ct);

        return Ok(await ContractOfAsync(form, role, ct));
    }

    private async Task<FormEditRules> RulesForAsync(Guid formId, CancellationToken ct) =>
        await FormEditorMapping.MembershipOfAsync(_workflows, formId, ct) is { IsPublished: true } membership
            ? new FormEditRules(membership.WorkflowName, membership.LockedQuestions.Select(question => question.QuestionId))
            : FormEditRules.None;

    private async Task<FormContract> ContractOfAsync(Form form, CollaboratorRole role, CancellationToken ct)
    {
        var collaboratorIds = form.Collaborators.Where(c => c.Role != CollaboratorRole.None).Select(c => c.UserId).ToList();
        var users = await _users.GetUsersAsync(collaboratorIds, ct);
        var membership = await FormEditorMapping.MembershipOfAsync(_workflows, form.Id, ct);

        return FormEditorMapping.ContractOf(form, users, role, FormEditorMapping.WorkflowRefOf(membership));
    }

    private async Task<FormDraftContract?> LegacyDraftOfAsync(Guid formId, Guid userId, CancellationToken ct)
    {
        try
        {
            var draft = await _drafts.GetFormDraftAsync(formId, userId, ct);
            return draft.Status == ServiceStatus.Success ? draft.Data : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Eski form taslağı okunamadı; stüdyo taslaksız açılıyor");
            return null;
        }
    }

    private static FormUpsertRequest UpsertOf(FormDocument document) => new(
        null,
        document.Title,
        document.Description,
        document.Schema,
        document.AllowAnonymousResponses,
        document.AllowMultipleResponses,
        document.RequiresManualReview,
        document.Status,
        null,
        document.Task,
        document.ClosesAt,
        document.TimeLimitMinutes);

    private static ServiceResult<T> Ok<T>(T data) => new(ServiceStatus.Success, Data: data);

    private static ServiceResult<T> Fail<T>(ServiceStatus status, string? message) => new(status, Message: message);
}

using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Caching;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Application.Validators;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Services.Forms;

public sealed record FormPublicationResult(ServiceResult<FormContract>? Failure, bool SchemaChanged = false, bool ClearResponseDrafts = false);

internal static class FormPublication
{
    public static async Task<FormPublicationResult> ApplyAsync(Form form, FormUpsertRequest contract, IFormWorkflowRepository workflows, bool guestUploadsEnabled, CancellationToken ct)
    {
        var schema = contract.Schema ?? new();

        var validation = FormValidator.ValidateUpsert(contract, schema, guestUploadsEnabled);
        if (validation.Status != ServiceStatus.Success)
            return Failed(validation.Status, validation.Message);

        var membership = await FormEditorMapping.MembershipOfAsync(workflows, form.Id, ct);

        if (membership is { IsPublished: true } && FindWorkflowLockViolation(contract, membership) is { } lockViolation)
            return Failed(ServiceStatus.NotAcceptable, lockViolation);

        var task = NormalizeTask(contract.Task);
        var timeLimit = NormalizeTimeLimit(contract.TimeLimitMinutes);

        if (timeLimit.HasValue && membership is { IsPublished: true, AllowMultipleRuns: true })
            return Failed(ServiceStatus.NotAcceptable, $"Bu form '{membership.WorkflowName}' akışında kullanılıyor ve akış tekrar başlatılabiliyor; kişisel süre kullanılamaz.");

        var timing = FormValidator.ValidateTiming(contract.AllowAnonymousResponses, membership is { IsPublished: true } ? false : contract.AllowMultipleResponses, task, timeLimit);
        if (timing.Status != ServiceStatus.Success)
            return Failed(timing.Status, timing.Message);

        var schemaChanged = !FormSchemaChange.IsSame(form.Schema, schema);

        var clearResponseDrafts = !form.HasTimeLimit
            && (timeLimit.HasValue || form.Status != contract.Status || FormSchemaChange.BreaksDrafts(form.Schema, schema));

        form.Title = contract.Title;
        form.Description = contract.Description;
        form.Schema = schema;
        form.Status = contract.Status;

        form.AllowAnonymousResponses = contract.AllowAnonymousResponses;
        form.AllowMultipleResponses = contract.AllowMultipleResponses;
        form.RequiresManualReview = contract.RequiresManualReview;
        form.Task = task;
        form.ClosesAt = UtcDate.Normalize(contract.ClosesAt);
        form.TimeLimitMinutes = timeLimit;
        form.Brand = contract.Brand;

        return new FormPublicationResult(null, schemaChanged, clearResponseDrafts);
    }

    public static async Task AfterCommitAsync(Guid formId, FormPublicationResult result, IFormDraftService drafts, ICacheService cache, CancellationToken ct)
    {
        await drafts.ClearFormDraftsAsync(formId, ct);

        if (result.ClearResponseDrafts)
            await drafts.ClearResponseDraftsAsync(formId, ct);

        if (result.SchemaChanged)
            await cache.TryRemoveAsync(FormCacheKeys.Analytics(formId), ct);
    }

    public static FormTask? NormalizeTask(FormTask? task) =>
        task is null || string.IsNullOrWhiteSpace(task.Content)
            ? null
            : new FormTask { Content = task.Content, Collapsible = task.Collapsible, Downloadable = task.Downloadable };

    public static int? NormalizeTimeLimit(int? minutes) => minutes is > 0 ? minutes : null;

    private static FormPublicationResult Failed(ServiceStatus status, string? message) =>
        new(new ServiceResult<FormContract>(status, Message: message));

    /// <summary>
    /// Yayınlanmış bir akışta kullanılan formda yalnız yönlendirmeyi bozan
    /// değişiklikler engellenir; soru ekleme, metin düzeltme ve sıralama serbesttir.
    /// </summary>
    private static string? FindWorkflowLockViolation(FormUpsertRequest contract, WorkflowFormMembership workflowLock)
    {
        if (contract.Status != FormStatus.Open)
            return $"Bu form '{workflowLock.WorkflowName}' akışında kullanılıyor; kapatılamaz.";

        if (contract.AllowAnonymousResponses)
            return $"Bu form '{workflowLock.WorkflowName}' akışında kullanılıyor; anonim yanıtlara açılamaz.";

        // Yalnız sorunun şemada kalması denetlenir. Soru metnini değiştirmek serbesttir:
        // koşullar soruyu id ile okur. Seçenek adları istemci tarafında korunur, çünkü
        // backend şema içindeki seçenek listesini okumaz.
        var questionIds = (contract.Schema ?? new()).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var missing = workflowLock.LockedQuestions.FirstOrDefault(question => !questionIds.Contains(question.QuestionId));

        return missing is null
            ? null
            : $"'{missing.QuestionId}' sorusu '{workflowLock.WorkflowName}' akışının yönlendirme koşullarında kullanılıyor; formdan çıkarılamaz.";
    }
}

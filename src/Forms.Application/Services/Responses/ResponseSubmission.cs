using System.Net;
using Microsoft.Extensions.Logging;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Attribution;
using Skylab.Forms.Application.Caching;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Attempts;
using Skylab.Forms.Application.Contracts.Responses;
using Skylab.Forms.Application.Contracts.Workflows;
using Skylab.Forms.Application.Services.Attempts;
using Skylab.Forms.Application.Services.GuestUploads;
using Skylab.Forms.Application.Services.Workflows;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Services.Responses;

public interface IResponseSubmission
{
    Task<ServiceResult<ResponseSubmitResult>> SubmitResponseAsync(ResponseSubmitRequest contract, Guid? userId, IPAddress? clientAddress, CancellationToken cancellationToken = default);
}

public sealed class ResponseSubmission : IResponseSubmission
{
    private readonly IFormRepository _forms;
    private readonly IFormResponseRepository _responses;
    private readonly IFormsUnitOfWork _uow;
    private readonly IFormDraftService _draftService;
    private readonly ICacheService _cache;
    private readonly IFormMailNotifier _mailNotifier;
    private readonly IFormWorkflowRuntime _workflowRuntime;
    private readonly IFormAttemptService _attempts;
    private readonly IGuestUploadService _guestUploads;
    private readonly ILogger<ResponseSubmission> _logger;
    private readonly TimeProvider _time;

    public ResponseSubmission(
        IFormRepository forms,
        IFormResponseRepository responses,
        IFormsUnitOfWork uow,
        IFormDraftService draftService,
        ICacheService cache,
        IFormMailNotifier mailNotifier,
        IFormWorkflowRuntime workflowRuntime,
        IFormAttemptService attempts,
        IGuestUploadService guestUploads,
        ILogger<ResponseSubmission> logger,
        TimeProvider time)
    {
        _forms = forms;
        _responses = responses;
        _uow = uow;
        _draftService = draftService;
        _cache = cache;
        _mailNotifier = mailNotifier;
        _workflowRuntime = workflowRuntime;
        _attempts = attempts;
        _guestUploads = guestUploads;
        _logger = logger;
        _time = time;
    }

    public async Task<ServiceResult<ResponseSubmitResult>> SubmitResponseAsync(ResponseSubmitRequest contract, Guid? userId, IPAddress? clientAddress, CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var form = await _forms.GetByIdAsync(contract.FormId, cancellationToken);
        if (form == null) return new ServiceResult<ResponseSubmitResult>(ServiceStatus.NotFound, Message: "Form bulunamadı.");
        switch (form.AvailabilityAt(now))
        {
            case FormAvailability.Closed:
                return new ServiceResult<ResponseSubmitResult>(ServiceStatus.NotAvailable, Message: "Bu form şu anda yanıt kabul etmiyor.");
            case FormAvailability.ClosesAtPassed:
                return new ServiceResult<ResponseSubmitResult>(
                    ServiceStatus.NotAvailable,
                    new ResponseSubmitResult(null, null, 0, Reason: FormClosedReason.Closed),
                    "Bu form kapanış saatinde kendiliğinden kapandı.");
        }

        if (!form.AllowAnonymousResponses && userId == null) return new ServiceResult<ResponseSubmitResult>(ServiceStatus.Unauthorized, Message: "Bu formu doldurmak için giriş yapmalısınız.");

        ResponseGuest? guest = null;

        if (userId == null && ResponseGuest.IsAskedBy(form.Schema))
        {
            guest = ResponseGuest.From(form.Schema, contract.Responses);
            if (guest is null) return new ServiceResult<ResponseSubmitResult>(ServiceStatus.NotAcceptable, Message: "Ad, soyad ve e-posta zorunludur.");
        }
        else if (userId == null)
        {
            guest = ResponseGuest.FromEmailQuestion(form.Schema, contract.Responses);
        }

        var guestGate = userId == null
            ? await _guestUploads.CheckSubmitAsync(form, contract, clientAddress, cancellationToken)
            : await _guestUploads.CheckAccountSubmitAsync(form, contract, userId.Value, cancellationToken);
        if (guestGate.Rejection is not null) return guestGate.Rejection;

        FormAttempt? attempt = null;

        if (userId.HasValue && form.HasTimeLimit)
        {
            var gate = await _attempts.CheckSubmitAsync(form, userId.Value, cancellationToken);
            if (gate.Rejection is not null) return gate.Rejection;

            attempt = gate.Attempt;
        }

        var response = MapToEntity(form, contract.Responses, contract.TimeSpent, userId, contract.Attribution, guest, now);

        if (attempt is not null) _attempts.MarkSubmitted(attempt, response.Id);

        if (userId.HasValue)
        {
            var workflowResult = await SubmitThroughWorkflowAsync(form, response, userId.Value, cancellationToken);

            if (workflowResult is not null)
            {
                return workflowResult;
            }
        }

        if (userId.HasValue && !form.AllowMultipleResponses && attempt is null)
        {
            var hasExistingResponse = await _responses.HasNonArchivedResponseAsync(form.Id, userId.Value, cancellationToken);
            if (hasExistingResponse) return new ServiceResult<ResponseSubmitResult>(ServiceStatus.NotAcceptable, Message: "Bu formu daha önce doldurdunuz.");
        }

        if (!guestGate.Verified) response.Status = FormResponseStatus.Flagged;

        response.IsSingleResponse = userId.HasValue && !form.AllowMultipleResponses && attempt is null;

        IReadOnlyList<GuestAttachment> attachments = [];
        if (guestGate.Files.Count > 0)
        {
            var attach = await _guestUploads.AttachAsync(guestGate.Files, response.Id, cancellationToken);
            if (attach.Rejection is not null) return attach.Rejection;

            attachments = attach.Attachments;
        }

        try
        {
            _responses.Add(response);
            await _uow.SaveChangesAsync(cancellationToken);
        }
        catch (StorageConflictException) when (response.IsSingleResponse)
        {
            if (!await _responses.HasNonArchivedResponseAsync(form.Id, userId!.Value, cancellationToken)) throw;

            return new ServiceResult<ResponseSubmitResult>(ServiceStatus.NotAcceptable, Message: "Bu formu daha önce doldurdunuz.");
        }
        catch
        {
            await _guestUploads.DetachAsync(attachments);
            throw;
        }

        if (guestGate.SessionId is { } guestSessionId)
            await AfterCommitAsync("misafir yükleme oturumunu kapatma", response.Id, () => _guestUploads.ConsumeAsync(guestSessionId, guestGate.Files), cancellationToken);

        await AfterResponseSavedAsync(form, response, cancellationToken, sendCopy: guestGate.Verified);

        var underReview = form.RequiresManualReview || response.Status == FormResponseStatus.Flagged;
        var status = underReview ? ServiceStatus.PendingApproval : ServiceStatus.Success;
        var message = underReview ? "Yanıtınız incelemeye alındı." : "Yanıt kaydedildi.";

        var result = new ResponseSubmitResult(response.Id, LinkedFormId: null, Step: 0);
        return new ServiceResult<ResponseSubmitResult>(status, Data: result, Message: message);
    }

    private async Task<ServiceResult<ResponseSubmitResult>?> SubmitThroughWorkflowAsync(
        Form form,
        FormResponse response,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var workflow = await _workflowRuntime.SubmitAsync(form, response, userId, cancellationToken);

        if (workflow.Data is { State: WorkflowActionState.NotInWorkflow }) return null;

        if (workflow.Data is not { } outcome)
            return new ServiceResult<ResponseSubmitResult>(workflow.Status, Message: workflow.Message);

        var rejected = workflow.Status.IsFailure();

        if (!rejected) await AfterResponseSavedAsync(form, response, cancellationToken);

        var result = new ResponseSubmitResult(
            rejected ? null : response.Id,
            LinkedFormId: outcome.IsLegacyTwoStepFlow ? outcome.FormId : null,
            LegacyStep.From(outcome),
            outcome.InstanceId,
            outcome.State,
            outcome.Stage,
            outcome.FormId,
            outcome.StartFormId,
            outcome.Reason,
            outcome.Journey);

        return new ServiceResult<ResponseSubmitResult>(workflow.Status, result, workflow.Message);
    }

    private async Task AfterResponseSavedAsync(Form form, FormResponse response, CancellationToken cancellationToken, bool sendCopy = true)
    {
        await _cache.TryRemoveAsync(FormCacheKeys.Analytics(form.Id), cancellationToken);

        if (response.UserId is { } userId)
            await AfterCommitAsync("taslağı silme", response.Id, () => _draftService.DeleteResponseDraftAsync(form.Id, userId, cancellationToken), cancellationToken);

        if (sendCopy)
            await AfterCommitAsync("kopya maili", response.Id, () => _mailNotifier.NotifyResponseCopyAsync(form, response, cancellationToken), cancellationToken);
    }

    private async Task AfterCommitAsync(string effect, Guid responseId, Func<Task> run, CancellationToken ct)
    {
        try
        {
            await run();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Cevap {ResponseId} kaydedildi ama {Effect} yapılamadı", responseId, effect);
        }
    }

    private static FormResponse MapToEntity(Form form, List<FormResponseSchemaItem> userResponses, int? timeSpent, Guid? userId, ResponseAttributionRequest? attribution, ResponseGuest? guest, DateTime submittedAt)
    {
        var responseData = new List<FormResponseSchemaItem>();

        foreach (var schemaItem in form.Schema)
        {
            var userAnswer = userResponses.FirstOrDefault(r => r.Id == schemaItem.Id)?.Answer ?? string.Empty;

            string questionText = "";
            if (schemaItem.Props.TryGetValue("question", out var qVal) && qVal != null) questionText = qVal.ToString() ?? "";

            responseData.Add(new FormResponseSchemaItem
            {
                Id = schemaItem.Id,
                Type = schemaItem.Type,
                Question = questionText,
                Answer = userAnswer
            });
        }

        return new FormResponse
        {
            FormId = form.Id,
            UserId = userId,
            Data = responseData,
            TimeSpent = timeSpent,
            Status = form.RequiresManualReview ? FormResponseStatus.Pending : FormResponseStatus.NonRestrict,
            SubmittedAt = submittedAt,
            Attribution = AttributionNormalizer.Normalize(attribution),
            Guest = guest
        };
    }
}

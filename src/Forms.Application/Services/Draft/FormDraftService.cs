using System.Text.Json;
using System.Text.Json.Nodes;
using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Caching;
using Skylab.Forms.Application.Contracts.Draft;
using Skylab.Forms.Application.GuestUploads;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Services;

public class FormDraftService : IFormDraftService
{
    private readonly ICacheService _cache;
    private readonly IFormRepository _forms;
    private readonly IFormAttemptRepository _attempts;
    private readonly IDraftFileHolds _fileHolds;

    private static readonly TimeSpan ResponseDraftTtl = TimeSpan.FromHours(168);
    private static readonly TimeSpan FormDraftTtl = TimeSpan.FromHours(168);
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public FormDraftService(ICacheService cache, IFormRepository forms, IFormAttemptRepository attempts, IDraftFileHolds fileHolds)
    {
        _cache = cache;
        _forms = forms;
        _attempts = attempts;
        _fileHolds = fileHolds;
    }

    public async Task<ServiceResult<bool>> SaveResponseDraftAsync(Guid formId, Guid userId, ResponseDraftRequest draft, CancellationToken ct)
    {
        var form = await _forms.GetByIdAsync(formId, ct);
        if (form is null || form.Status != FormStatus.Open)
            return new ServiceResult<bool>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        if (form.HasTimeLimit)
        {
            var attempt = await _attempts.GetLatestAsync(formId, userId, ct);
            if (attempt is null || !attempt.AcceptsSubmissionAt(DateTime.UtcNow, AttemptLimits.SubmitGrace))
                return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Süre işlemediği için taslak kaydedilmedi.");
        }

        var key = FormCacheKeys.ResponseDraft(formId, userId);

        if (!HasAnswers(draft.Responses))
        {
            await _cache.RemoveAsync(key, ct);
            await HoldFilesAsync(form, userId, null, ct);
            return new ServiceResult<bool>(ServiceStatus.Success, Data: true);
        }

        var stored = new ResponseDraftContract(draft.Responses, draft.TimeSpent, DateTime.UtcNow, form.HasTimeLimit ? draft.Submission : null);
        await _cache.SetAsync(key, stored, DraftTtlFor(form), ct);
        await HoldFilesAsync(form, userId, stored, ct);

        return new ServiceResult<bool>(ServiceStatus.Success, Data: true);
    }

    public async Task RestoreResponseDraftAsync(Guid formId, Guid userId, List<FormResponseSchemaItem> responses, int timeSpent, List<FormResponseSchemaItem>? submission, CancellationToken ct = default)
    {
        var form = await _forms.GetByIdAsync(formId, ct);
        var stored = new ResponseDraftContract(responses, timeSpent, DateTime.UtcNow, submission);

        await _cache.SetAsync(FormCacheKeys.ResponseDraft(formId, userId), stored, form is null ? ResponseDraftTtl : DraftTtlFor(form), ct);

        if (form is not null) await HoldFilesAsync(form, userId, stored, ct);
    }

    public async Task<bool> HoldsFileAsync(Guid formId, Guid userId, Guid mediaId, CancellationToken ct = default)
    {
        // Süre kaydırılmadan okunur: arka plan denetimi taslağın ömrünü uzatmamalı.
        var draft = await _cache.GetAsync<ResponseDraftContract>(FormCacheKeys.ResponseDraft(formId, userId), ct: ct);

        return draft is not null && AnswersOf(draft).Any(answer => Guid.TryParse(answer.Answer, out var id) && id == mediaId);
    }

    private Task HoldFilesAsync(Form form, Guid userId, ResponseDraftContract? draft, CancellationToken ct)
    {
        var questions = form.Schema.Where(GuestUploadRules.IsFileQuestion).Select(item => item.Id).ToHashSet();
        if (questions.Count == 0) return Task.CompletedTask;

        IReadOnlyCollection<Guid> files = draft is null
            ? []
            : [.. AnswersOf(draft)
                .Where(answer => questions.Contains(answer.Id))
                .Select(answer => Guid.TryParse(answer.Answer, out var mediaId) ? mediaId : Guid.Empty)
                .Where(mediaId => mediaId != Guid.Empty)
                .Distinct()];

        return _fileHolds.HoldAsync(form.Id, userId, files, ct);
    }

    // Süreli formda gönderime hazır kopya da tutulur ve süre dolunca geçici cevap ondan kurulur; dosyayı o da taşır.
    private static IEnumerable<FormResponseSchemaItem> AnswersOf(ResponseDraftContract draft) =>
        ResponseDataMapper.FromDraft(draft.Responses).Concat(ResponseDataMapper.FromDraft(draft.Submission ?? []));

    private static TimeSpan DraftTtlFor(Form form)
    {
        if (form.TimeLimitMinutes is not { } minutes) return ResponseDraftTtl;

        var timed = TimeSpan.FromMinutes(minutes) + TimeSpan.FromDays(1);
        return timed > ResponseDraftTtl ? timed : ResponseDraftTtl;
    }
    public async Task<ServiceResult<ResponseDraftContract?>> GetResponseDraftAsync(Guid formId, Guid userId, CancellationToken ct = default)
    {
        var key = FormCacheKeys.ResponseDraft(formId, userId);

        var draft = await _cache.GetAsync<ResponseDraftContract>(key, ResponseDraftTtl, ct);

        if (draft != null && !HasAnswers(draft.Responses))
        {
            await _cache.RemoveAsync(key, ct);
            await _fileHolds.HoldAsync(formId, userId, [], ct);
            draft = null;
        }

        if (draft == null)
            return new ServiceResult<ResponseDraftContract?>(ServiceStatus.NotFound, Message: "Yanıt taslağı bulunamadı.");

        // Bağlar okurken de taslağa göre tazelenir: aynı anda giden iki kayıttan eskisi sonra yazılmış
        // ya da taslak bağlar tutulmadan önce kaydedilmiş olabilir.
        if (await _forms.GetByIdAsync(formId, ct) is { } form)
            await HoldFilesAsync(form, userId, draft, ct);

        return new ServiceResult<ResponseDraftContract?>(ServiceStatus.Success, Data: draft);
    }

    /// <summary>
    /// Taslak cevapları istemcinin JSON olarak yazdığı değerlerdir; boş metin, liste,
    /// nesne ve kapalı anahtar (false) cevap sayılmaz. JSON olmayan eski düz metin cevaptır.
    /// </summary>
    private static bool HasAnswers(IEnumerable<FormResponseSchemaItem>? responses) =>
        responses?.Any(item => !IsBlankAnswer(item.Answer)) == true;

    private static bool IsBlankAnswer(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return true;

        try
        {
            using var document = JsonDocument.Parse(answer);
            return IsBlank(document.RootElement);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsBlank(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.False => true,
        JsonValueKind.String => string.IsNullOrWhiteSpace(element.GetString()),
        JsonValueKind.Array => element.EnumerateArray().All(IsBlank),
        JsonValueKind.Object => element.EnumerateObject().All(property => IsBlank(property.Value)),
        _ => false
    };
    public async Task<ServiceResult<bool>> DeleteResponseDraftAsync(Guid formId, Guid userId, CancellationToken ct = default)
    {
        var key = FormCacheKeys.ResponseDraft(formId, userId);

        await _cache.RemoveAsync(key, ct);
        await _fileHolds.HoldAsync(formId, userId, [], ct);

        return new ServiceResult<bool>(ServiceStatus.Success, Data: true, Message: "Taslak silindi.");
    }
    public async Task<ServiceResult<bool>> ClearResponseDraftsAsync(Guid formId, CancellationToken ct = default)
    {
        var prefix = FormCacheKeys.ResponseDraftPrefix(formId);

        await _cache.RemoveByPrefixAsync(prefix, ct);
        await _fileHolds.ReleaseFormAsync(formId, ct);

        return new ServiceResult<bool>(ServiceStatus.Success, Data: true, Message: "Forma ait tüm yanıt taslakları temizlendi.");
    }

    public async Task<ServiceResult<bool>> SaveFormDraftAsync(Guid formId, Guid userId, FormDraftRequest draft, CancellationToken ct = default)
    {
        var key = FormCacheKeys.FormDraft(formId, userId);

        await _cache.SetAsync(key, draft, FormDraftTtl, ct);

        return new ServiceResult<bool>(ServiceStatus.Success, Data: true);
    }

    public async Task<ServiceResult<FormDraftContract?>> GetFormDraftAsync(Guid formId, Guid userId, CancellationToken ct = default)
    {
        var key = FormCacheKeys.FormDraft(formId, userId);

        var draftRequest = await _cache.GetAsync<FormDraftRequest>(key, FormDraftTtl, ct);

        if (draftRequest == null)
            return new ServiceResult<FormDraftContract?>(ServiceStatus.NotFound, Message: "Form taslağı bulunamadı.");

        var form = await _forms.GetByIdAsync(formId, ct);
        if (form != null && IsDraftIdenticalToForm(draftRequest.Data, form))
        {
            await _cache.RemoveAsync(key, ct);
            return new ServiceResult<FormDraftContract?>(ServiceStatus.NotFound, Message: "Form taslağı bulunamadı.");
        }

        return new ServiceResult<FormDraftContract?>(ServiceStatus.Success, Data: draftRequest.Data);
    }

    /// <summary>
    /// jsonb anahtar sırasını değiştirdiği için şemalar metin olarak değil, ağaç olarak karşılaştırılır.
    /// </summary>
    private static bool IsDraftIdenticalToForm(FormDraftContract draft, Form form)
    {
        if (draft.Title != form.Title) return false;
        if ((draft.Description ?? "") != (form.Description ?? "")) return false;
        if (draft.AllowAnonymousResponses != form.AllowAnonymousResponses) return false;
        if (draft.AllowMultipleResponses != form.AllowMultipleResponses) return false;
        if (draft.RequiresManualReview != form.RequiresManualReview) return false;
        if (draft.Status != form.Status) return false;
        if (draft.TimeLimitMinutes != form.TimeLimitMinutes) return false;
        if (draft.ClosesAt != form.ClosesAt) return false;
        if (JsonSerializer.Serialize(draft.Task, CamelCase) != JsonSerializer.Serialize(form.Task, CamelCase)) return false;

        return JsonNode.DeepEquals(ToJsonNode(draft.Schema), ToJsonNode(form.Schema));
    }

    private static JsonNode? ToJsonNode(IReadOnlyList<FormSchemaItem>? schema) =>
        JsonNode.Parse(JsonSerializer.Serialize(schema, CamelCase));

    public async Task<ServiceResult<bool>> DeleteFormDraftAsync(Guid formId, Guid userId, CancellationToken ct = default)
    {
        var key = FormCacheKeys.FormDraft(formId, userId);

        await _cache.RemoveAsync(key, ct);

        return new ServiceResult<bool>(ServiceStatus.Success, Data: true, Message: "Form taslağı silindi.");
    }
    public async Task<ServiceResult<bool>> ClearFormDraftsAsync(Guid formId, CancellationToken ct = default)
    {
        var prefix = FormCacheKeys.FormDraftPrefix(formId);

        await _cache.RemoveByPrefixAsync(prefix, ct);

        return new ServiceResult<bool>(ServiceStatus.Success, Data: true, Message: "Forma ait tüm düzenleme taslakları temizlendi.");
    }
}

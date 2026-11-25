using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Contracts.ResponseReads;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;

namespace Skylab.Forms.Application.Services.ResponseReads;

public class ResponseReadService : IResponseReadService
{
    private readonly IFormRepository _forms;
    private readonly IFormResponseRepository _responses;
    private readonly TimeProvider _time;

    public ResponseReadService(IFormRepository forms, IFormResponseRepository responses, TimeProvider time)
    {
        _forms = forms;
        _responses = responses;
        _time = time;
    }

    public async Task<FormReadContract?> GetFormAsync(Guid formId, CancellationToken ct = default)
    {
        var form = await _forms.GetByIdAsync(formId, ct);
        if (form is null) return null;

        var questions = form.Schema
            .Select(item => new QuestionReadContract(item.Id, item.Type, ResponseDataMapper.QuestionOf(item), PropText(item, "inputType"), PropText(item, "identity")))
            .ToList();

        return new FormReadContract(form.Id, form.Title, questions);
    }

    public async Task<ResponseReadPage?> ListAsync(Guid formId, ResponseReadQuery query, CancellationToken ct = default)
    {
        if (!await _forms.ExistsAsync(formId, ct)) return null;

        // Saat sorgudan önce alınır: sorgu sürerken değişen cevap bir sonraki changedSince'te yine gelir.
        var now = _time.GetUtcNow().UtcDateTime;
        var after = query.After is { } cursor ? (cursor.ChangedAt, cursor.Id) : ((DateTime, Guid)?)null;
        var rows = await _responses.GetChangedPageAsync(formId, query.AcceptedOnly, query.ChangedSince, after, query.Limit + 1, ct);

        var page = rows.Take(query.Limit).ToList();
        var items = page.Select(response => ContractOf(response, query.Fields, withGuest: false)).ToList();

        if (rows.Count <= query.Limit)
            return new ResponseReadPage(items, null, now);

        var last = page[^1];
        return new ResponseReadPage(items, new ResponseReadCursor(ChangedAt(last), last.Id).ToString(), null);
    }

    public async Task<ResponseReadContract?> GetAsync(Guid formId, Guid responseId, IReadOnlyList<string> fields, CancellationToken ct = default)
    {
        var response = await _responses.GetByIdWithFormAndCollaboratorsAsync(responseId, ct);
        if (response is null || response.FormId != formId || response.Status == FormResponseStatus.Provisional || response.UserId == DeletedUser.Id)
            return null;

        return ContractOf(response, fields, withGuest: true);
    }

    private static ResponseReadContract ContractOf(FormResponse response, IReadOnlyList<string> fields, bool withGuest)
    {
        // Adsız misafir gitmez: core misafir biletini ad ve soyadsız yazmaz.
        var guest = response.Guest is { HasName: true } named ? named : null;

        return new ResponseReadContract(
            response.FormId,
            response.Id,
            StatusOf(response.Status),
            response.UserId,
            guest is not null,
            response.SubmittedAt,
            ChangedAt(response),
            fields.Count == 0 ? null : AnswersTo(response.Data, fields),
            withGuest && guest is not null ? new GuestReadContract(guest.FirstName, guest.LastName, guest.Email) : null);
    }

    /// <summary>Yalnız istenen soruların cevapları: core'a eşlenmemiş bir cevap (telefon, kimlik no) gitmez.</summary>
    private static Dictionary<string, string> AnswersTo(IReadOnlyList<FormResponseSchemaItem> data, IReadOnlyList<string> fields) =>
        data.Where(answer => fields.Contains(answer.Id))
            .GroupBy(answer => answer.Id)
            .ToDictionary(group => group.Key, group => group.First().Answer?.Trim() ?? string.Empty);

    // Core'a giden cevap bildirimiyle aynı eşleme; geçici cevap listeye hiç girmez.
    private static string StatusOf(FormResponseStatus status) => status switch
    {
        FormResponseStatus.NonRestrict or FormResponseStatus.Approved => "accepted",
        FormResponseStatus.Pending or FormResponseStatus.Flagged => "pending",
        _ => "declined"
    };

    private static DateTime ChangedAt(FormResponse response) => response.ReviewedAt ?? response.SubmittedAt;

    private static string? PropText(FormSchemaItem item, string key) =>
        item.Props.TryGetValue(key, out var value) ? value?.ToString() : null;
}

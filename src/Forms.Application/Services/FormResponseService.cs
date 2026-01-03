using Forms.Domain.Entities;
using Forms.Domain.Enums;
using Forms.Domain.Models;
using Forms.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Forms.Application.Contracts;
using Forms.Application.Contracts.Responses;

namespace Forms.Application.Services;

public class FormResponseService : IFormResponseService
{
    private readonly AppDbContext _context;

    public FormResponseService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceResult<Guid>> SubmitResponseAsync(ResponseSubmitRequest contract, Guid? userId, CancellationToken cancellationToken = default)
    {
        var form = await _context.Forms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == contract.FormId, cancellationToken);
        if (form == null) return new ServiceResult<Guid>(FormAccessStatus.NotFound, Message: "Form bulunamadı.");
        if (form.Status != FormStatus.Open) return new ServiceResult<Guid>(FormAccessStatus.NotAcceptable, Message: "Form kapalı.");

        if (!form.AllowAnonymousResponses && userId == null) return new ServiceResult<Guid>(FormAccessStatus.NotAuthorized, Message: "Bu formu doldurmak için giriş yapmalısınız.");

        if (userId.HasValue && !form.AllowMultipleResponses)
        {
            var hasExistingResponse = await _context.Responses.AnyAsync(r => r.FormId == form.Id && r.UserId == userId, cancellationToken);
            if (hasExistingResponse) return new ServiceResult<Guid>(FormAccessStatus.NotAcceptable, Message: "Bu formu daha önce doldurdunuz.");
        }

        var parentForm = await _context.Forms.AsNoTracking().FirstOrDefaultAsync(f => f.LinkedFormId == form.Id, cancellationToken);

        if (parentForm != null && userId.HasValue)
        {
            var parentResponse = await _context.Responses.Where(r => r.FormId == parentForm.Id && r.UserId == userId).OrderByDescending(r => r.SubmittedAt).FirstOrDefaultAsync(cancellationToken);

            if (parentResponse == null || parentResponse.Status != FormResponseStatus.Approved)
            {
                return new ServiceResult<Guid>(FormAccessStatus.RequiresParentApproval, Message: "Bu formu doldurmak için önceki aşamanın onaylanması gerekmektedir.");
            }
        }
        var response = MapToEntity(form, contract.Responses, userId);

        _context.Responses.Add(response);
        await _context.SaveChangesAsync(cancellationToken);

        return new ServiceResult<Guid>(!form.AllowAnonymousResponses ? FormAccessStatus.PendingApproval : FormAccessStatus.Available, Data: response.Id, Message: "Yanıt kaydedildi.");
    }
    public async Task<ServiceResult<PagedResult<ResponseSummaryContract>>> GetFormResponsesAsync(Guid formId, Guid userId, GetResponsesRequest request, CancellationToken cancellationToken = default)
    {
        var isAuthorized = await _context.Collaborators.AnyAsync(c => c.FormId == formId && c.UserId == userId && (c.Role != CollaboratorRole.None), cancellationToken);

        if (!isAuthorized) return new ServiceResult<PagedResult<ResponseSummaryContract>>(FormAccessStatus.NotAuthorized, Message: "Bu formun yanıtlarını görüntüleme yetkiniz yok.");

        var query = _context.Responses.AsNoTracking().Where(r => r.FormId == formId);

        switch (request.ResponderType)
        {
            case FormResponderType.Registered:
                query = query.Where(r => r.UserId != null);
                break;
            case FormResponderType.Anonymous:
                query = query.Where(r => r.UserId == null);
                break;
            case FormResponderType.All:
            default:
                break;
        }

        if (request.Status.HasValue)
            query = query.Where(r => r.Status == request.Status.Value);

        if (request.FilterByUserId.HasValue)
            query = query.Where(r => r.UserId == request.FilterByUserId.Value);

        if (request.SortingDirection == "ascending") { query = query.OrderBy(r => r.SubmittedAt); }
        else { query = query.OrderByDescending(r => r.SubmittedAt); }


        var totalResponseCount = await query.CountAsync(cancellationToken);

        var items = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(r => new ResponseSummaryContract(
                r.Id,
                r.UserId,
                r.Status,
                r.ReviewedBy,
                r.SubmittedAt,
                r.ReviewedAt
            )).ToListAsync(cancellationToken);
        
        var resultData = new PagedResult<ResponseSummaryContract>(
            items,
            totalResponseCount,
            request.Page,
            request.PageSize
        );

        return new ServiceResult<PagedResult<ResponseSummaryContract>>(FormAccessStatus.Available, Data: resultData);
    }
    public async Task<ServiceResult<ResponseContract>> GetResponseByIdAsync(Guid responseId, Guid userId, CancellationToken cancellationToken = default)
    {
        var response = await _context.Responses.AsNoTracking().Include(r => r.Form).ThenInclude(f => f.Collaborators).FirstOrDefaultAsync(r => r.Id == responseId, cancellationToken);

        if (response == null)
            return new ServiceResult<ResponseContract>(FormAccessStatus.NotFound, Message: "Yanıt bulunamadı.");

        var isAuthorized = response.Form.Collaborators.Any(c => c.UserId == userId && (c.Role != CollaboratorRole.None));

        if (!isAuthorized)
            return new ServiceResult<ResponseContract>(FormAccessStatus.NotAuthorized, Message: "Bu yanıtı görüntüleme yetkiniz yok.");

        FormRelationshipStatus relationshipStatus = FormRelationshipStatus.None;
        Guid? targetLinkedFormId = null;

        if (response.Form.LinkedFormId.HasValue)
        {
            relationshipStatus = FormRelationshipStatus.Parent;
            targetLinkedFormId = response.Form.LinkedFormId.Value;
        }
        else
        {
            var parentFormId = await _context.Forms.AsNoTracking()
                .Where(f => f.LinkedFormId == response.FormId)
                .Select(f => f.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (parentFormId != Guid.Empty)
            {
                relationshipStatus = FormRelationshipStatus.Child;
                targetLinkedFormId = parentFormId;
            }
        }

        Guid? linkedResponseId = null;

        if (targetLinkedFormId.HasValue && response.UserId.HasValue)
        {
            linkedResponseId = await _context.Responses.AsNoTracking()
                .Where(r => r.FormId == targetLinkedFormId.Value && r.UserId == response.UserId)
                .OrderByDescending(r => r.SubmittedAt)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new ServiceResult<ResponseContract>(
            FormAccessStatus.Available,
            Data: MapToDetailContract(response, relationshipStatus, linkedResponseId)
        );
    }
    public async Task<ServiceResult<bool>> UpdateResponseStatusAsync(ResponseStatusUpdateRequest contract, Guid reviewerId, CancellationToken cancellationToken = default)
    {
        var response = await _context.Responses.Include(r => r.Form).ThenInclude(f => f.Collaborators).FirstOrDefaultAsync(r => r.Id == contract.ResponseId, cancellationToken);

        if (response == null)
            return new ServiceResult<bool>(FormAccessStatus.NotFound, Message: "İlgili yanıt bulunamadı.");

        var isAuthorized = response.Form.Collaborators.Any(c => c.UserId == reviewerId && (c.Role != CollaboratorRole.None));

        if (!isAuthorized)
            return new ServiceResult<bool>(FormAccessStatus.NotAuthorized, Message: "Bu yanıtı onaylama veya reddetme yetkiniz yok.");

        response.Status = contract.NewStatus;
        response.ReviewedBy = reviewerId;
        response.ReviewNote = contract.Note;
        response.ReviewedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return new ServiceResult<bool>(FormAccessStatus.Available, Data: true, Message: "Yanıt durumu başarıyla güncellendi.");
    }
    private static FormResponse MapToEntity(Form form, List<FormResponseSchemaItem> userResponses, Guid? userId)
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
                Question = questionText,
                Answer = userAnswer
            });
        }

        return new FormResponse
        {
            FormId = form.Id,
            UserId = userId,
            Data = responseData,
            Status = form.AllowAnonymousResponses ? FormResponseStatus.NonRestrict : FormResponseStatus.Pending,
            SubmittedAt = DateTime.UtcNow
        };
    }
    private static ResponseContract MapToDetailContract(FormResponse response, FormRelationshipStatus relationshipStatus, Guid? linkedResponseId)
    {
        return new ResponseContract(
            response.Id,
            response.FormId,
            response.UserId,
            response.ReviewedBy,
            response.Data,
            response.Status,
            relationshipStatus,
            response.ReviewNote,
            linkedResponseId,
            response.SubmittedAt,
            response.ReviewedAt
        );
    }
}
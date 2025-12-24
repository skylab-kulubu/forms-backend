using Forms.Application.Contracts;
using Forms.Domain.Entities;
using Forms.Domain.Enums;
using Forms.Domain.Models;
using Forms.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Forms.Application.Services;

public class FormResponseService : IFormResponseService
{
    private readonly AppDbContext _context;

    public FormResponseService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceResult<Guid>> SubmitResponseAsync(FormResponseSubmitContract contract, Guid? userId, CancellationToken cancellationToken = default)
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

        if (form.LinkedFormId.HasValue && userId.HasValue)
        {
            var parentResponse = await _context.Responses.Where(r => r.FormId == form.LinkedFormId.Value && r.UserId == userId).OrderByDescending(r => r.SubmittedAt).FirstOrDefaultAsync(cancellationToken);

            if (parentResponse == null || parentResponse.Status != FormResponseStatus.Approved)
                return new ServiceResult<Guid>(FormAccessStatus.RequiresParentApproval, Message: "Bu formu doldurmak için önceki aşamanın onaylanması gerekmektedir.");
        }

        var response = MapToEntity(form, contract.Responses, userId);

        _context.Responses.Add(response);
        await _context.SaveChangesAsync(cancellationToken);

        return new ServiceResult<Guid>(FormAccessStatus.Available, Data: response.Id, Message: "Yanıt kaydedildi.");
    }
    public async Task<ServiceResult<List<FormResponseSummaryContract>>> GetFormResponsesAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default)
    {
        var isAuthorized = await _context.Collaborators.AnyAsync(c => c.FormId == formId && c.UserId == userId && (c.Role != CollaboratorRole.None), cancellationToken);

        if (!isAuthorized) return new ServiceResult<List<FormResponseSummaryContract>>(FormAccessStatus.NotAuthorized, Message: "Bu formun yanıtlarını görüntüleme yetkiniz yok.");
        
        var responses = await _context.Responses.AsNoTracking().Where(r => r.FormId == formId).OrderByDescending(r => r.SubmittedAt)
            .Select(r => new FormResponseSummaryContract(
                r.Id,
                r.UserId,
                r.Status,
                r.SubmittedAt,
                r.ReviewedAt
            ))
            .ToListAsync(cancellationToken);

        return new ServiceResult<List<FormResponseSummaryContract>>(FormAccessStatus.Available, Data: responses);
    }
    public async Task<ServiceResult<FormResponseDetailContract>> GetResponseByIdAsync(Guid responseId, Guid userId, CancellationToken cancellationToken = default)
    {
        var response = await _context.Responses.AsNoTracking().Include(r => r.Form).ThenInclude(f => f.Collaborators).FirstOrDefaultAsync(r => r.Id == responseId, cancellationToken);

        if (response == null) 
            return new ServiceResult<FormResponseDetailContract>(FormAccessStatus.NotFound, Message: "Yanıt bulunamadı.");

        var isAuthorized = response.Form.Collaborators.Any(c => c.UserId == userId && (c.Role != CollaboratorRole.None));
        var isResponseOwner = response.UserId == userId;

        if (!isAuthorized && !isResponseOwner) 
            return new ServiceResult<FormResponseDetailContract>(FormAccessStatus.NotAuthorized, Message: "Bu yanıtı görüntüleme yetkiniz yok.");

        Guid? parentResponseId = null;
        if (response.Form.LinkedFormId.HasValue && response.UserId.HasValue)
        {
            parentResponseId = await _context.Responses.AsNoTracking()
                .Where(r => r.FormId == response.Form.LinkedFormId.Value && r.UserId == response.UserId)
                .Where(r => r.Status == FormResponseStatus.Approved)
                .OrderByDescending(r => r.SubmittedAt).Select(r => r.Id).FirstOrDefaultAsync(cancellationToken);
        }

        return new ServiceResult<FormResponseDetailContract>(
            FormAccessStatus.Available, 
            Data: MapToDetailContract(response, parentResponseId)
        );
    } 
    public async Task<ServiceResult<bool>> UpdateResponseStatusAsync(FormResponseStatusUpdateContract contract, Guid reviewerId, CancellationToken cancellationToken = default)
    {
        var response = await _context.Responses.Include(r => r.Form).ThenInclude(f => f.Collaborators).FirstOrDefaultAsync(r => r.Id == contract.ResponseId, cancellationToken);

        if (response == null) 
            return new ServiceResult<bool>(FormAccessStatus.NotFound, Message: "İlgili yanıt bulunamadı.");

        var isAuthorized = response.Form.Collaborators.Any(c => c.UserId == reviewerId && (c.Role != CollaboratorRole.None));
        
        if (!isAuthorized) 
            return new ServiceResult<bool>(FormAccessStatus.NotAuthorized, Message: "Bu yanıtı onaylama veya reddetme yetkiniz yok.");

        response.Status = contract.NewStatus;
        response.ReviewedBy = reviewerId;
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
    private static FormResponseDetailContract MapToDetailContract(FormResponse response, Guid? parentResponseId)
    {
        return new FormResponseDetailContract(
            response.Id,
            response.FormId,
            response.UserId,
            response.Data,
            response.Status,
            parentResponseId,
            response.SubmittedAt,
            response.ReviewedAt
        );
    }
}
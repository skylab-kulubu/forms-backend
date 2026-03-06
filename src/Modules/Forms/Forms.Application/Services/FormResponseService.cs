using Skylab.Shared.Application.Contracts;
using Skylab.Shared.Domain.Enums;
using Skylab.Exports.Application.Contracts;
using Skylab.Exports.Application.Services;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;
using Skylab.Forms.Domain.Models;
using Skylab.Forms.Infrastructure.Storage;
using Skylab.Forms.Application.Contracts;
using Skylab.Forms.Application.Contracts.Auth;
using Skylab.Forms.Application.Contracts.Responses;
using Microsoft.EntityFrameworkCore;

namespace Skylab.Forms.Application.Services;

public class FormResponseService : IFormResponseService
{
    private readonly FormsDbContext _context;
    private readonly IExternalUserService _userService;
    private readonly IExcelService _excelService;

    public FormResponseService(FormsDbContext context, IExternalUserService userService, IExcelService excelService)
    {
        _context = context;
        _userService = userService;
        _excelService = excelService;
    }

    public async Task<ServiceResult<Guid>> SubmitResponseAsync(ResponseSubmitRequest contract, Guid? userId, CancellationToken cancellationToken = default)
    {
        var form = await _context.Forms.AsNoTracking().FirstOrDefaultAsync(f => f.Id == contract.FormId, cancellationToken);
        if (form == null) return new ServiceResult<Guid>(ServiceStatus.NotFound, Message: "Form bulunamadı.");
        if (form.Status != FormStatus.Open) return new ServiceResult<Guid>(ServiceStatus.NotAcceptable, Message: "Form kapalı.");

        if (!form.AllowAnonymousResponses && userId == null) return new ServiceResult<Guid>(ServiceStatus.Unauthorized, Message: "Bu formu doldurmak için giriş yapmalısınız.");

        if (userId.HasValue && !form.AllowMultipleResponses)
        {
            var hasExistingResponse = await _context.Responses.AnyAsync(r => r.FormId == form.Id && r.UserId == userId && !r.IsArchived, cancellationToken);
            if (hasExistingResponse) return new ServiceResult<Guid>(ServiceStatus.NotAcceptable, Message: "Bu formu daha önce doldurdunuz.");
        }

        var parentForm = await _context.Forms.AsNoTracking().FirstOrDefaultAsync(f => f.LinkedFormId == form.Id, cancellationToken);

        if (parentForm != null && userId.HasValue)
        {
            var parentResponse = await _context.Responses.Where(r => r.FormId == parentForm.Id && r.UserId == userId).OrderByDescending(r => r.SubmittedAt).FirstOrDefaultAsync(cancellationToken);

            if (parentResponse == null)
                return new ServiceResult<Guid>(ServiceStatus.RequiresParentApproval, Message: "Bu formu doldurmak için önceki aşamayı doldurmanız gerekmektedir.");

            if (parentForm.RequiresManualReview && parentResponse.Status != FormResponseStatus.Approved)
                return new ServiceResult<Guid>(ServiceStatus.RequiresParentApproval, Message: "Bu formu doldurmak için önceki aşamanın onaylanması gerekmektedir.");
        }
        var response = MapToEntity(form, contract.Responses, contract.TimeSpent, userId);

        _context.Responses.Add(response);
        await _context.SaveChangesAsync(cancellationToken);

        return new ServiceResult<Guid>(form.RequiresManualReview ? ServiceStatus.PendingApproval : ServiceStatus.Success, Data: response.Id, Message: "Yanıt kaydedildi.");
    }
    public async Task<ServiceResult<FormResponsesListResult>> GetFormResponsesAsync(Guid formId, Guid userId, GetResponsesRequest request, CancellationToken cancellationToken = default)
    {
        var isAuthorized = await _context.Collaborators.AnyAsync(c => c.FormId == formId && c.UserId == userId && (c.Role != CollaboratorRole.None), cancellationToken);

        if (!isAuthorized) return new ServiceResult<FormResponsesListResult>(ServiceStatus.NotAuthorized, Message: "Bu formun yanıtlarını görüntüleme yetkiniz yok.");

        var query = _context.Responses.AsNoTracking().Where(r => r.FormId == formId);

        bool showArchived = request.ShowArchived.GetValueOrDefault(false);

        if (showArchived) { query = query.Where(r => r.IsArchived == true); }
        else { query = query.Where(r => r.IsArchived == false); }

        if (request.Status.HasValue) { query = query.Where(r => r.Status == request.Status.Value); }

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

        if (request.FilterByUserId.HasValue)
            query = query.Where(r => r.UserId == request.FilterByUserId.Value);

        if (request.SortingDirection == "ascending") { query = query.OrderBy(r => r.SubmittedAt); }
        else { query = query.OrderByDescending(r => r.SubmittedAt); }


        var totalResponseCount = await query.CountAsync(cancellationToken);
        var averageTimeSpent = await query.AverageAsync(r => r.TimeSpent, cancellationToken);

        var items = await query.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(r => new
            {
                r.Id,
                r.UserId,
                r.Status,
                r.IsArchived,
                r.ReviewedBy,
                r.ArchivedBy,
                r.SubmittedAt,
                r.ReviewedAt,
                r.ArchivedAt
            }).ToListAsync(cancellationToken);

        var userIds = items.Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value).Distinct().ToList();
        var users = await _userService.GetUsersAsync(userIds, cancellationToken);

        var mappedItems = items.Select(r =>
        {
            var userDetail = r.UserId.HasValue ? users.FirstOrDefault(u => u.Id == r.UserId) : null;
            var reviewerDetail = r.ReviewedBy.HasValue ? users.FirstOrDefault(u => u.Id == r.ReviewedBy) : null;

            if (userDetail == null && r.UserId.HasValue)
                userDetail = new UserContract(r.UserId.Value, null, "??", null);

            if (reviewerDetail == null && r.ReviewedBy.HasValue)
                reviewerDetail = new UserContract(r.ReviewedBy.Value, null, "??", null);

            return new ResponseSummaryContract(r.Id, userDetail, r.Status, r.IsArchived, reviewerDetail, r.ArchivedBy, r.SubmittedAt, r.ReviewedAt, r.ArchivedAt);
        }).ToList();

        var resultData = new PagedResult<ResponseSummaryContract>(
            mappedItems,
            totalResponseCount,
            request.Page,
            request.PageSize
        );

        var finalResult = new FormResponsesListResult(resultData, averageTimeSpent);

        return new ServiceResult<FormResponsesListResult>(ServiceStatus.Success, Data: finalResult);
    }
    public async Task<ServiceResult<ResponseContract>> GetResponseByIdAsync(Guid responseId, Guid userId, CancellationToken cancellationToken = default)
    {
        var response = await _context.Responses.AsNoTracking().Include(r => r.Form).ThenInclude(f => f.Collaborators).FirstOrDefaultAsync(r => r.Id == responseId, cancellationToken);

        if (response == null)
            return new ServiceResult<ResponseContract>(ServiceStatus.NotFound, Message: "Yanıt bulunamadı.");

        var isAuthorized = response.Form.Collaborators.Any(c => c.UserId == userId && (c.Role != CollaboratorRole.None));

        if (!isAuthorized)
            return new ServiceResult<ResponseContract>(ServiceStatus.NotAuthorized, Message: "Bu yanıtı görüntüleme yetkiniz yok.");

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

        var userIds = new List<Guid>();
        if (response.UserId.HasValue) userIds.Add(response.UserId.Value);
        if (response.ReviewedBy.HasValue) userIds.Add(response.ReviewedBy.Value);
        if (response.ArchivedBy.HasValue) userIds.Add(response.ArchivedBy.Value);

        var users = await _userService.GetUsersAsync(userIds, cancellationToken);

        var responderUser = response.UserId.HasValue ? users.FirstOrDefault(u => u.Id == response.UserId) : null;
        var reviewerUser = response.ReviewedBy.HasValue ? users.FirstOrDefault(u => u.Id == response.ReviewedBy) : null;
        var archiverUser = response.ArchivedBy.HasValue ? users.FirstOrDefault(u => u.Id == response.ArchivedBy) : null;

        return new ServiceResult<ResponseContract>(
            ServiceStatus.Success,
            Data: MapToDetailContract(response, relationshipStatus, linkedResponseId, responderUser, reviewerUser, archiverUser)
        );
    }
    public async Task<ServiceResult<bool>> UpdateResponseStatusAsync(ResponseStatusUpdateRequest contract, Guid reviewerId, CancellationToken cancellationToken = default)
    {
        var response = await _context.Responses.Include(r => r.Form).ThenInclude(f => f.Collaborators).FirstOrDefaultAsync(r => r.Id == contract.ResponseId, cancellationToken);

        if (response == null)
            return new ServiceResult<bool>(ServiceStatus.NotFound, Message: "İlgili yanıt bulunamadı.");

        var isAuthorized = response.Form.Collaborators.Any(c => c.UserId == reviewerId && (c.Role != CollaboratorRole.None));

        if (!isAuthorized)
            return new ServiceResult<bool>(ServiceStatus.NotAuthorized, Message: "Bu yanıtı onaylama veya reddetme yetkiniz yok.");

        if (response.IsArchived)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Arşivlenmiş yanıtlar üzerinde değişiklik yapılamaz.");

        response.Status = contract.NewStatus;
        response.ReviewedBy = reviewerId;
        response.ReviewNote = contract.Note;
        response.ReviewedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return new ServiceResult<bool>(ServiceStatus.Success, Data: true, Message: "Yanıt durumu başarıyla güncellendi.");
    }
    public async Task<ServiceResult<bool>> ArchiveResponseAsync(Guid responseId, Guid archiverId, CancellationToken cancellationToken = default)
    {
        var response = await _context.Responses.Include(r => r.Form).ThenInclude(f => f.Collaborators).FirstOrDefaultAsync(r => r.Id == responseId, cancellationToken);

        if (response == null)
            return new ServiceResult<bool>(ServiceStatus.NotFound, Message: "İlgili yanıt bulunamadı.");

        var isAuthorized = response.Form.Collaborators.Any(c => c.UserId == archiverId && (c.Role != CollaboratorRole.None));

        if (!isAuthorized)
            return new ServiceResult<bool>(ServiceStatus.NotAuthorized, Message: "Bu yanıtı arşivleme yetkiniz yok.");

        if (response.IsArchived)
            return new ServiceResult<bool>(ServiceStatus.NotAcceptable, Message: "Bu yanıt zaten arşivlenmiş.");

        if (response.Status == FormResponseStatus.Pending)
        {
            response.Status = FormResponseStatus.Declined;
            response.ReviewNote = "Arşivlendiği için sistem tarafından otomatik olarak reddedildi.";
            response.ReviewedBy = archiverId;
            response.ReviewedAt = DateTime.UtcNow;
        }
        
        response.IsArchived = true;
        response.ArchivedBy = archiverId;
        response.ArchivedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync(cancellationToken);
        return new ServiceResult<bool>(ServiceStatus.Success, Data: true, Message: "Yanıt başarıyla arşivlendi.");
    }
    public async Task<ServiceResult<byte[]>> ExportResponsesToExcelAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _context.Forms.AsNoTracking().Include(f => f.Collaborators).Include(f => f.Responses.Where(r => !r.IsArchived)).FirstOrDefaultAsync(f => f.Id == formId, cancellationToken);

        if (form == null)
            return new ServiceResult<byte[]>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        var isAuthorized = form.Collaborators.Any(c => c.UserId == userId && c.Role != CollaboratorRole.None);
        if (!isAuthorized)
            return new ServiceResult<byte[]>(ServiceStatus.NotAuthorized, Message: "Bu formun yanıtlarını dışa aktarma yetkiniz yok.");
        
        var headers = new List<string>
        {
            "Yanıt ID",
            "Kullanıcı ID",
            "Gönderim Tarihi",
            "Durum",
            "İncelenme Notu"
        };

        foreach (var schemaItem in form.Schema)
        {
            string questionText = schemaItem.Props.TryGetValue("question", out var qVal) ? qVal?.ToString() ?? "İsimsiz Soru" : "Soru";
            headers.Add(questionText);
        };

        var rows = new List<List<string>>();

        foreach (var response in form.Responses.OrderBy(r => r.SubmittedAt))
        {
            var row = new List<string>
            {
                response.Id.ToString(),
                response.UserId?.ToString() ?? "Anonim",
                response.SubmittedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                response.Status.ToString(),
                response.ReviewNote ?? ""
            };

            foreach (var schemaItem in form.Schema)
            {
                var answerItem = response.Data.FirstOrDefault(d => d.Id == schemaItem.Id);
                row.Add(answerItem?.Answer ?? string.Empty);
            }

            rows.Add(row);
        }

        string sheetName = form.Title.Length > 31 ? form.Title.Substring(0, 31) : form.Title;

        var exportRequest = new ExcelExportRequest(sheetName, headers, rows);
        return _excelService.GenerateExcel(exportRequest);
    }
    private static FormResponse MapToEntity(Form form, List<FormResponseSchemaItem> userResponses, int? timeSpent, Guid? userId)
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
            SubmittedAt = DateTime.UtcNow
        };
    }
    private static ResponseContract MapToDetailContract(FormResponse response, FormRelationshipStatus relationshipStatus, Guid? linkedResponseId, UserContract? responderUser, UserContract? reviewerUser, UserContract? archiverUser)
    {
        return new ResponseContract(
            response.Id,
            response.FormId,
            responderUser,
            reviewerUser,
            archiverUser,
            response.Data,
            response.TimeSpent,
            response.Status,
            response.IsArchived,
            relationshipStatus,
            response.ReviewNote,
            linkedResponseId,
            response.SubmittedAt,
            response.ReviewedAt,
            response.ArchivedAt
        );
    }
}
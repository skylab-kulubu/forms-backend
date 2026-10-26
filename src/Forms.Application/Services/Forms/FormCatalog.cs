using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Services.Forms;

public interface IFormCatalog
{
    Task<ServiceResult<FormContract>> GetFormByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<FormInfoContract>> GetFormInfoByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<PagedResult<FormSummaryContract>>> GetUserFormsAsync(Guid userId, GetUserFormsRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<PagedResult<FormAllSummaryContract>>> GetAllFormsAsync(GetAllFormsRequest request, CancellationToken cancellationToken = default);
}

public sealed class FormCatalog : IFormCatalog
{
    private readonly IFormRepository _forms;
    private readonly IFormResponseRepository _responses;
    private readonly IUserDirectory _userService;
    private readonly ICurrentUser _currentUser;
    private readonly IFormWorkflowRepository _workflows;

    public FormCatalog(
        IFormRepository forms,
        IFormResponseRepository responses,
        IUserDirectory userService,
        ICurrentUser currentUser,
        IFormWorkflowRepository workflows)
    {
        _forms = forms;
        _responses = responses;
        _userService = userService;
        _currentUser = currentUser;
        _workflows = workflows;
    }

    public async Task<ServiceResult<FormContract>> GetFormByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetWithCollaboratorsAsync(id, cancellationToken);

        if (form == null) return new ServiceResult<FormContract>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        if (!form.Allows(userId, _currentUser.IsPlatformAdmin, FormAction.OpenEditor))
            return new ServiceResult<FormContract>(ServiceStatus.NotAuthorized, Message: "Yetkiniz yok.");

        var userRole = form.RoleOf(userId);

        var collaboratorIds = form.Collaborators.Where(c => c.Role != CollaboratorRole.None).Select(c => c.UserId).ToList();
        var users = await _userService.GetUsersAsync(collaboratorIds, cancellationToken);

        return new ServiceResult<FormContract>(
            ServiceStatus.Success,
            Data: FormEditorMapping.ContractOf(form, users, userRole, FormEditorMapping.WorkflowRefOf(await FormEditorMapping.MembershipOfAsync(_workflows, id, cancellationToken))));
    }

    public async Task<ServiceResult<FormInfoContract>> GetFormInfoByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetWithCollaboratorsAsync(id, cancellationToken);

        if (form == null) return new ServiceResult<FormInfoContract>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        if (!form.Allows(userId, _currentUser.IsPlatformAdmin, FormAction.ViewForm))
            return new ServiceResult<FormInfoContract>(ServiceStatus.NotAuthorized, Message: "Yetkiniz yok.");

        var counts = await _responses.GetCountsAsync(id, cancellationToken);

        var contract = new FormInfoContract(
            form.Id,
            form.Title,
            form.Status,
            form.UpdatedAt ?? form.CreatedAt,
            counts.Total,
            counts.Waiting,
            AverageTimeSeconds: counts.AverageTimeSpentSeconds,
            LastSeenUsers: Array.Empty<FormLastSeenUserContract>(),
            UserRole: form.RoleOf(userId),
            TimeLimitMinutes: form.HasTimeLimit ? form.TimeLimitMinutes : null
        );

        return new ServiceResult<FormInfoContract>(ServiceStatus.Success, Data: contract);
    }

    public async Task<ServiceResult<PagedResult<FormSummaryContract>>> GetUserFormsAsync(Guid userId, GetUserFormsRequest request, CancellationToken cancellationToken = default)
    {
        var data = await _forms.GetUserFormsAsync(userId, request, cancellationToken);

        // Akış adımları listeden gizlenmez; hangi formun akışa ait olduğunu istemci
        // bu alandan görüp kendi süzmesini yapar.
        var memberships = await _workflows.GetFormMembershipsAsync(
            [.. data.Items.Select(form => form.Id)], cancellationToken);

        var items = data.Items
            .Select(form => form with
            {
                Workflow = memberships.TryGetValue(form.Id, out var membership) ? FormEditorMapping.WorkflowRefOf(membership) : null
            })
            .ToList();

        return new ServiceResult<PagedResult<FormSummaryContract>>(
            ServiceStatus.Success,
            Data: new PagedResult<FormSummaryContract>(items, data.TotalCount, data.Page, data.PageSize));
    }

    public async Task<ServiceResult<PagedResult<FormAllSummaryContract>>> GetAllFormsAsync(GetAllFormsRequest request, CancellationToken cancellationToken = default)
    {
        var raw = await _forms.GetAllFormsAsync(request, cancellationToken);

        var ownerIds = raw.Items.Select(f => f.OwnerUserId).Distinct().ToList();
        var users = await _userService.GetUsersAsync(ownerIds, cancellationToken);
        var userMap = users.ToDictionary(u => u.Id);

        var memberships = await _workflows.GetFormMembershipsAsync(
            [.. raw.Items.Select(form => form.Id)], cancellationToken);

        var forms = raw.Items.Select(f =>
        {
            userMap.TryGetValue(f.OwnerUserId, out var owner);
            return new FormAllSummaryContract(
                f.Id,
                f.Title,
                f.Status,
                owner ?? new UserContract(f.OwnerUserId, null, null, null),
                f.AllowAnonymousResponses,
                f.AllowMultipleResponses,
                f.RequiresManualReview,
                memberships.TryGetValue(f.Id, out var membership) ? FormEditorMapping.WorkflowRefOf(membership) : null,
                f.CreatedAt,
                f.UpdatedAt,
                f.ResponseCount
            );
        }).ToList();

        return new ServiceResult<PagedResult<FormAllSummaryContract>>(
            ServiceStatus.Success,
            Data: new PagedResult<FormAllSummaryContract>(forms, raw.TotalCount, raw.Page, raw.PageSize)
        );
    }
}

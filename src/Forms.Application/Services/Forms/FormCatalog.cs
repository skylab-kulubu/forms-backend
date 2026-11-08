using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.Identity;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Caching;
using Skylab.Forms.Application.Contracts.Forms;
using Skylab.Forms.Domain.Common;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Services.Forms;

public interface IFormCatalog
{
    Task<ServiceResult<FormContract>> GetFormByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<FormInfoContract>> GetFormInfoByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<PagedResult<FormSummaryContract>>> GetUserFormsAsync(Guid userId, GetUserFormsRequest request, CancellationToken cancellationToken = default);
    Task<ServiceResult<FormAllPageContract>> GetAllFormsAsync(GetAllFormsRequest request, CancellationToken cancellationToken = default);
}

public sealed class FormCatalog : IFormCatalog
{
    private static readonly TimeSpan OwnersCacheTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan OwnersMissCacheTtl = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan OwnerLookupLimit = TimeSpan.FromSeconds(3);
    private const int TrendDays = 7;

    private readonly IFormRepository _forms;
    private readonly IFormResponseRepository _responses;
    private readonly IUserDirectory _userService;
    private readonly ICurrentUser _currentUser;
    private readonly IFormWorkflowRepository _workflows;
    private readonly ICacheService _cache;
    private readonly TimeProvider _time;

    public FormCatalog(
        IFormRepository forms,
        IFormResponseRepository responses,
        IUserDirectory userService,
        ICurrentUser currentUser,
        IFormWorkflowRepository workflows,
        ICacheService cache,
        TimeProvider time)
    {
        _forms = forms;
        _responses = responses;
        _userService = userService;
        _currentUser = currentUser;
        _workflows = workflows;
        _cache = cache;
        _time = time;
    }

    public async Task<ServiceResult<FormContract>> GetFormByIdAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
    {
        var form = await _forms.GetWithCollaboratorsAsync(id, cancellationToken);

        if (form == null) return new ServiceResult<FormContract>(ServiceStatus.NotFound, Message: "Form bulunamadı.");

        if (!form.Allows(userId, _currentUser.IsPlatformAdmin, FormAction.ViewForm))
            return new ServiceResult<FormContract>(ServiceStatus.NotAuthorized, Message: "Yetkiniz yok.");

        var userRole = form.RoleOf(userId);

        var collaboratorIds = form.Collaborators.Where(c => c.Role != CollaboratorRole.None).Select(c => c.UserId).ToList();
        var users = await _userService.GetUsersAsync(collaboratorIds, cancellationToken);

        return new ServiceResult<FormContract>(
            ServiceStatus.Success,
            Data: FormEditorMapping.ContractOf(form, users, userRole, FormEditorMapping.WorkflowRefOf(await FormEditorMapping.MembershipOfAsync(_workflows, id, cancellationToken))) with
            {
                Access = FormEditorMapping.AccessOf(form, userId, _currentUser.IsPlatformAdmin)
            });
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
            TimeLimitMinutes: form.HasTimeLimit ? form.TimeLimitMinutes : null,
            Access: FormEditorMapping.AccessOf(form, userId, _currentUser.IsPlatformAdmin)
        );

        return new ServiceResult<FormInfoContract>(ServiceStatus.Success, Data: contract);
    }

    public async Task<ServiceResult<PagedResult<FormSummaryContract>>> GetUserFormsAsync(Guid userId, GetUserFormsRequest request, CancellationToken cancellationToken = default)
    {
        var data = await _forms.GetUserFormsAsync(userId, request, cancellationToken);

        // Akış adımları listeden gizlenmez; hangi formun akışa ait olduğunu istemci
        // bu alandan görüp kendi süzmesini yapar.
        var formIds = data.Items.Select(form => form.Id).ToList();
        var memberships = await _workflows.GetFormMembershipsAsync(formIds, cancellationToken);
        var owners = await GetOwnersAsync(data.Items.Select(form => form.CreatedBy?.Id ?? Guid.Empty), cancellationToken);
        var days = await GetLastWeekAsync(formIds, cancellationToken);

        var items = data.Items
            .Select(form => form with
            {
                Workflow = memberships.TryGetValue(form.Id, out var membership) ? FormEditorMapping.WorkflowRefOf(membership) : null,
                CreatedBy = form.CreatedBy is { } owner ? owners.GetValueOrDefault(owner.Id, owner) : null,
                Last7Days = days.TryGetValue(form.Id, out var counts) ? counts : [.. new int[TrendDays]]
            })
            .ToList();

        return new ServiceResult<PagedResult<FormSummaryContract>>(
            ServiceStatus.Success,
            Data: new PagedResult<FormSummaryContract>(items, data.TotalCount, data.Page, data.PageSize));
    }

    public async Task<ServiceResult<FormAllPageContract>> GetAllFormsAsync(GetAllFormsRequest request, CancellationToken cancellationToken = default)
    {
        var catalog = await _forms.GetCatalogRowsAsync(cancellationToken);
        var directory = await GetOwnerDirectoryAsync(catalog, cancellationToken);

        var sortsByOwner = string.Equals(request.SortBy?.Trim(), "owner", StringComparison.OrdinalIgnoreCase);
        var raw = await _forms.GetAllFormsAsync(request, sortsByOwner ? OwnerOrderOf(directory) : null, cancellationToken);

        var unknown = raw.Items.Select(f => f.OwnerUserId).Distinct().Where(id => directory.All(user => user.Id != id));
        var users = directory.Concat(await _userService.GetUsersAsync(unknown, cancellationToken));
        var userMap = users.DistinctBy(u => u.Id).ToDictionary(u => u.Id);

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
                f.ResponseCount,
                f.PendingCount,
                f.LastResponseAt,
                f.OwnerUserId == DeletedUser.Id
            );
        }).ToList();

        return new ServiceResult<FormAllPageContract>(
            ServiceStatus.Success,
            Data: new FormAllPageContract(forms, raw.TotalCount, raw.Page, raw.PageSize, SummaryOf(catalog), OwnersOf(catalog, directory))
        );
    }

    private async Task<Dictionary<Guid, UserContract>> GetOwnersAsync(IEnumerable<Guid> ownerIds, CancellationToken cancellationToken)
    {
        var owners = new Dictionary<Guid, UserContract>();
        var missing = new List<Guid>();

        foreach (var id in ownerIds.Distinct().Where(id => id != Guid.Empty))
        {
            if (await _cache.TryGetAsync<UserContract>(FormCacheKeys.Owner(id), cancellationToken) is { } cached) owners[id] = cached;
            else missing.Add(id);
        }

        if (missing.Count == 0) return owners;

        using var lookup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lookup.CancelAfter(OwnerLookupLimit);

        List<UserContract> found;
        try
        {
            found = await _userService.GetUsersAsync(missing, lookup.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            found = [];
        }

        foreach (var user in found)
        {
            owners[user.Id] = user;
            await _cache.TrySetAsync(FormCacheKeys.Owner(user.Id), user, OwnersCacheTtl, cancellationToken);
        }

        foreach (var id in missing.Where(id => !owners.ContainsKey(id)))
            await _cache.TrySetAsync(FormCacheKeys.Owner(id), new UserContract(id, null, null, null), OwnersMissCacheTtl, cancellationToken);

        return owners;
    }

    private async Task<Dictionary<Guid, List<int>>> GetLastWeekAsync(IReadOnlyCollection<Guid> formIds, CancellationToken cancellationToken)
    {
        var firstDay = ClubTime.ToLocal(_time.GetUtcNow().UtcDateTime).Date.AddDays(1 - TrendDays);
        var submissions = await _responses.GetSubmittedSinceAsync(formIds, ClubTime.ToUtc(firstDay), cancellationToken);

        return submissions
            .GroupBy(submission => submission.FormId)
            .ToDictionary(group => group.Key, group =>
            {
                var counts = new int[TrendDays];
                foreach (var submission in group)
                {
                    var day = (ClubTime.ToLocal(submission.SubmittedAt).Date - firstDay).Days;
                    if (day is >= 0 and < TrendDays) counts[day]++;
                }
                return counts.ToList();
            });
    }

    private async Task<List<UserContract>> GetOwnerDirectoryAsync(IReadOnlyCollection<FormCatalogRow> catalog, CancellationToken cancellationToken)
    {
        var cached = await _cache.TryGetAsync<List<UserContract>>(FormCacheKeys.CatalogOwners, cancellationToken);
        if (cached is not null) return cached;

        var users = await _userService.GetUsersAsync(catalog.Select(form => form.OwnerUserId), cancellationToken);

        await _cache.TrySetAsync(FormCacheKeys.CatalogOwners, users, users.Count > 0 ? OwnersCacheTtl : OwnersMissCacheTtl, cancellationToken);

        return users;
    }

    private static List<Guid> OwnerOrderOf(IEnumerable<UserContract> directory) =>
        [.. directory
            .OrderBy(user => user.FullName ?? user.Email ?? string.Empty, StringComparer.Create(ClubTime.Culture, ignoreCase: true))
            .Select(user => user.Id)];

    private static FormCatalogSummaryContract SummaryOf(IReadOnlyCollection<FormCatalogRow> catalog)
    {
        var visible = catalog.Where(form => form.Status != FormStatus.Deleted).ToList();

        return new FormCatalogSummaryContract(
            visible.Count,
            visible.Count(form => form.Status == FormStatus.Open),
            visible.Count(form => form.Status == FormStatus.Closed),
            catalog.Count - visible.Count,
            visible.Count(form => form.OwnerUserId == DeletedUser.Id),
            visible.Sum(form => form.PendingCount));
    }

    private static List<FormOwnerContract> OwnersOf(IReadOnlyCollection<FormCatalogRow> catalog, IEnumerable<UserContract> directory)
    {
        var owned = catalog
            .Where(form => form.Status != FormStatus.Deleted && form.OwnerUserId != DeletedUser.Id)
            .GroupBy(form => form.OwnerUserId)
            .ToDictionary(group => group.Key, group => group.Count());

        return directory
            .Where(user => owned.ContainsKey(user.Id))
            .DistinctBy(user => user.Id)
            .OrderByDescending(user => owned[user.Id])
            .ThenBy(user => user.FullName, StringComparer.Create(ClubTime.Culture, ignoreCase: true))
            .Select(user => new FormOwnerContract(user.Id, user.Email, user.FullName, user.ProfilePictureUrl, user.FirstName, owned[user.Id]))
            .ToList();
    }
}

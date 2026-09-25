using Skylab.Forms.Application.Abstractions;
using Skylab.Forms.Application.Abstractions.Storage;
using Skylab.Forms.Application.Common;
using Skylab.Forms.Application.Contracts.ShortLinks;
using Skylab.Forms.Application.ShortLinks;
using Skylab.Forms.Domain.Entities;
using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Application.Services.ShortLinks;

public class FormShortLinkService : IFormShortLinkService
{
    private readonly IFormRepository _forms;
    private readonly ICoreShortLinks _links;
    private readonly IExternalUserService _users;
    private readonly ICurrentUserService _currentUserService;
    private readonly ShortLinkOptions _options;

    public FormShortLinkService(IFormRepository forms, ICoreShortLinks links, IExternalUserService users, ICurrentUserService currentUserService, ShortLinkOptions options)
    {
        _forms = forms;
        _links = links;
        _users = users;
        _currentUserService = currentUserService;
        _options = options;
    }

    public async Task<ServiceResult<FormShortLinkContract>> GetAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(formId, userId, requiresEdit: false, cancellationToken);
        if (access.Denied is not null) return new ServiceResult<FormShortLinkContract>(access.Denied.Value, Message: access.Message);

        var result = await _links.GetAsync(formId, cancellationToken);
        if (result.Status == CoreLinkStatus.NotFound) return new ServiceResult<FormShortLinkContract>(ServiceStatus.Success);
        return await ToResultAsync(result, cancellationToken);
    }

    public async Task<ServiceResult<FormShortLinkContract>> EnsureAsync(Guid formId, Guid userId, CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(formId, userId, requiresEdit: false, cancellationToken);
        if (access.Denied is not null) return new ServiceResult<FormShortLinkContract>(access.Denied.Value, Message: access.Message);

        var url = $"{_options.FormsPublicUrl.TrimEnd('/')}/{formId}";
        var title = access.Form!.Title;
        var result = await _links.EnsureAsync(formId, url, title, DefaultAlias(title), userId, cancellationToken);
        return await ToResultAsync(result, cancellationToken);
    }

    public async Task<ServiceResult<FormShortLinkContract>> RenameAsync(Guid formId, Guid userId, string? alias, CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(formId, userId, requiresEdit: true, cancellationToken);
        if (access.Denied is not null) return new ServiceResult<FormShortLinkContract>(access.Denied.Value, Message: access.Message);

        var result = await _links.RenameAsync(formId, alias?.Trim() ?? string.Empty, DefaultAlias(access.Form!.Title), cancellationToken);
        return await ToResultAsync(result, cancellationToken);
    }

    private static string DefaultAlias(string? title) => ShortLinkAlias.FromTitle(title, DateTime.UtcNow.Year);

    public async Task<ServiceResult<AliasAvailabilityContract>> CheckAliasAsync(Guid formId, Guid userId, string alias, CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(formId, userId, requiresEdit: true, cancellationToken);
        if (access.Denied is not null) return new ServiceResult<AliasAvailabilityContract>(access.Denied.Value, Message: access.Message);

        var answer = await _links.CheckAliasAsync(alias.Trim(), cancellationToken);
        if (answer is null)
            return new ServiceResult<AliasAvailabilityContract>(ServiceStatus.ServiceUnavailable, Message: "Kısa link servisine ulaşılamadı.");

        return new ServiceResult<AliasAvailabilityContract>(ServiceStatus.Success, Data: new AliasAvailabilityContract(answer.Alias, answer.Available, answer.Reason));
    }

    public async Task<ServiceResult<QrImageContract>> GetQrAsync(Guid formId, Guid userId, bool svg, CancellationToken cancellationToken = default)
    {
        var access = await AuthorizeAsync(formId, userId, requiresEdit: false, cancellationToken);
        if (access.Denied is not null) return new ServiceResult<QrImageContract>(access.Denied.Value, Message: access.Message);

        var link = await _links.GetAsync(formId, cancellationToken);
        if (link.Status == CoreLinkStatus.NotFound || link.Link is null)
            return new ServiceResult<QrImageContract>(link.Status == CoreLinkStatus.NotFound ? ServiceStatus.NotFound : ServiceStatus.ServiceUnavailable, Message: "Bu formun kısa linki yok.");

        var image = await _links.GetQrAsync(link.Link.Alias, svg, 1024, cancellationToken);
        if (image is null)
            return new ServiceResult<QrImageContract>(ServiceStatus.ServiceUnavailable, Message: "QR oluşturulamadı.");

        var fileName = $"{link.Link.Alias}-qr.{(svg ? "svg" : "png")}";
        return new ServiceResult<QrImageContract>(ServiceStatus.Success, Data: new QrImageContract(image.Content, image.ContentType, fileName));
    }

    private async Task<(Form? Form, ServiceStatus? Denied, string? Message)> AuthorizeAsync(Guid formId, Guid userId, bool requiresEdit, CancellationToken cancellationToken)
    {
        var form = await _forms.GetWithCollaboratorsAsync(formId, cancellationToken);
        if (form is null) return (null, ServiceStatus.NotFound, "Form bulunamadı.");

        var role = form.Collaborators.FirstOrDefault(c => c.UserId == userId)?.Role ?? CollaboratorRole.None;
        var allowed = requiresEdit
            ? role is CollaboratorRole.Owner or CollaboratorRole.Editor
            : role != CollaboratorRole.None;

        if (!allowed && !await _currentUserService.HasRoleAsync("skyforms:*", "forms", cancellationToken))
        {
            var message = requiresEdit ? "Kısa adı yalnız sahip ve editörler değiştirebilir." : "Bu formu paylaşma yetkiniz yok.";
            return (form, ServiceStatus.NotAuthorized, message);
        }

        return (form, null, null);
    }

    private async Task<ServiceResult<FormShortLinkContract>> ToResultAsync(CoreLinkResult result, CancellationToken cancellationToken)
    {
        return result.Status switch
        {
            CoreLinkStatus.Ok when result.Link is not null => new ServiceResult<FormShortLinkContract>(ServiceStatus.Success, Data: await ToContractAsync(result.Link, cancellationToken)),
            CoreLinkStatus.NotFound => new ServiceResult<FormShortLinkContract>(ServiceStatus.NotFound, Message: "Bu formun kısa linki yok."),
            CoreLinkStatus.Conflict => new ServiceResult<FormShortLinkContract>(ServiceStatus.Conflict, Message: "Bu kısa ad başka bir linkte kullanılıyor."),
            CoreLinkStatus.Invalid => new ServiceResult<FormShortLinkContract>(ServiceStatus.NotAcceptable, Message: "Kısa ad harf ya da rakamla başlamalı; yalnız harf, rakam, - ve _ içerebilir."),
            CoreLinkStatus.EventManaged => new ServiceResult<FormShortLinkContract>(ServiceStatus.NotAuthorized, Message: "Bu kısa ad etkinlik panelinden değiştiriliyor."),
            _ => new ServiceResult<FormShortLinkContract>(ServiceStatus.ServiceUnavailable, Message: "Kısa link servisine ulaşılamadı.")
        };
    }

    private async Task<FormShortLinkContract> ToContractAsync(CoreLink link, CancellationToken cancellationToken)
    {
        var creator = link.CreatedBy is { } creatorId ? await _users.GetUserAsync(creatorId, cancellationToken) : null;
        var managedByEvent = link.EventId is not null;
        return new FormShortLinkContract(
            link.Id,
            link.Alias,
            link.ClickCount,
            link.Label,
            managedByEvent,
            managedByEvent ? link.Label : null,
            creator,
            link.CreatedAt
        );
    }
}

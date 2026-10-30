using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Domain.Entities;

public enum FormAction
{
    ViewForm,

    OpenEditor,

    EditForm,
    DeleteForm,

    ViewResponses,

    HandleResponses,

    ViewShortLink,
    ManageShortLink,

    VoidApplication
}

public static class FormAccess
{
    public static bool Allows(CollaboratorRole role, bool isPlatformAdmin, FormAction action) => action switch
    {
        FormAction.ViewForm or FormAction.ViewResponses or FormAction.ViewShortLink => IsMember(role) || isPlatformAdmin,
        FormAction.HandleResponses => IsMember(role),
        FormAction.OpenEditor or FormAction.ManageShortLink => CanEdit(role) || isPlatformAdmin,
        FormAction.EditForm or FormAction.VoidApplication => CanEdit(role),
        FormAction.DeleteForm => role == CollaboratorRole.Owner,
        _ => false
    };

    private static bool IsMember(CollaboratorRole role) =>
        role is CollaboratorRole.Viewer or CollaboratorRole.Editor or CollaboratorRole.Owner;

    private static bool CanEdit(CollaboratorRole role) =>
        role is CollaboratorRole.Editor or CollaboratorRole.Owner;
}

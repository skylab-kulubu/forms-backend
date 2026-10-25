using Skylab.Forms.Domain.Enums;

namespace Skylab.Forms.Domain.Entities;

public enum FormAction
{
    /// <summary>Formun özetini görmek.</summary>
    ViewForm,

    /// <summary>Formu düzenleyicide açmak.</summary>
    OpenEditor,

    EditForm,
    DeleteForm,

    /// <summary>Yanıt listesi ve ayrıntısı, dosyalar, analiz, metrikler, dışa aktarım, mail kitlesi, denemeler.</summary>
    ViewResponses,

    /// <summary>Onaylamak, reddetmek, düzeltme istemek, arşivlemek, paylaşmak; süre uzatmak ve deneme kararları.</summary>
    HandleResponses,

    ViewShortLink,
    ManageShortLink,

    /// <summary>Bir akış başvurusunu geçersiz sayıp kişiye yeniden hak vermek.</summary>
    VoidApplication
}

/// <summary>
/// Bir formda kimin ne yapabileceği; bütün kural bu tabloda. Viewer "yanıt görüntüleyici"dir: yanıtları görür ve
/// onlar üzerinde karar verir ama formu düzenleyemez. Hizmet yöneticisi her şeyi okur, yanıtlar üzerinde karar vermez.
/// </summary>
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

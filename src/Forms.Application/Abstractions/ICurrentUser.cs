namespace Skylab.Forms.Application.Abstractions;

/// <summary>İsteği yapan kişi, Forms'un diliyle. Kimlik sağlayıcının rol biçimini Infrastructure çevirir.</summary>
public interface ICurrentUser
{
    /// <summary>Girişli kişinin kimliği; anonim istekte null.</summary>
    Guid? Id { get; }

    /// <summary>Bütün formları okuyabilen ve sahipsiz kalan içeriği yönetebilen hizmet yöneticisi.</summary>
    bool IsPlatformAdmin { get; }
}

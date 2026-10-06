namespace Skylab.Forms.Domain.Models;

/// <summary>
/// Etkinlik formunu giriş yapmadan dolduran misafirin kimlik alanlarına yazdıkları.
/// Kayıtlı kullanıcının yanıtında hiç kayıt olmaz; onun kimliği profilinden gelir.
/// </summary>
public class ResponseGuest
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

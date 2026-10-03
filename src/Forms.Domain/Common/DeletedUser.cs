namespace Skylab.Forms.Domain.Common;

/// <summary>
/// Hesabı silinen kişinin yerine yazılan ortak kimlik (hesap silme sözleşmesi §8).
/// Herkes için aynıdır; kişiye özel takma ad yoktur. Arayüz bu kimlik için core'a
/// ad sormaz, doğrudan <see cref="DisplayName"/> gösterir.
/// </summary>
public static class DeletedUser
{
    public static readonly Guid Id = Guid.Parse("00000000-0000-4000-8000-000000000000");

    public const string DisplayName = "Silinmiş kullanıcı";
}

namespace Skylab.Forms.Domain.Common;

/// <summary>Hesabı silinen herkesin yerine yazılan ortak kimlik (core hesap silme sözleşmesi §8).</summary>
public static class DeletedUser
{
    public static readonly Guid Id = Guid.Parse("00000000-0000-4000-8000-000000000000");
    public const string DisplayName = "Silinmiş kullanıcı";
}

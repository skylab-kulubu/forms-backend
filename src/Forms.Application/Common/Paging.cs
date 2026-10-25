namespace Skylab.Forms.Application.Common;

/// <summary>Sayfa ve boyut istemciden gelir; sorgu negatif bir OFFSET'e ya da sınırsız bir sayfaya dönüşmesin.</summary>
public static class Paging
{
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize));
}

using Skylab.Forms.Application.ShortLinks;

namespace Skylab.Forms.Tests.Application;

public class ShortLinkAliasTests
{
    [Theory]
    [InlineData("Yıldız Teknik Üniversitesi Başvuru", "yildiz-teknik-universitesi-2026")]
    [InlineData("Bahar 2025 Kampı", "bahar-2025-kampi")]
    [InlineData("Fatih'in Formu", "fatihin-formu-2026")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa-2026")]
    [InlineData("!!!", "form-2026")]
    [InlineData("   ", "form-2026")]
    [InlineData(null, "form-2026")]
    public void An_alias_is_a_readable_slug_of_the_title_and_the_year(string? title, string expected) =>
        Assert.Equal(expected, ShortLinkAlias.FromTitle(title, 2026));
}

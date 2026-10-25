using Skylab.Forms.Application.Attribution;
using Skylab.Forms.Application.Contracts.Responses;

namespace Skylab.Forms.Tests.Application;

public class AttributionNormalizerTests
{
    [Fact]
    public void A_known_source_alias_collapses_and_implies_its_medium()
    {
        var attribution = AttributionNormalizer.Normalize(new ResponseAttributionRequest(" IG ", null, "Bahar", null, null));

        Assert.NotNull(attribution);
        Assert.Equal(("instagram", "social", "bahar"), (attribution.Source, attribution.Medium, attribution.Campaign));
    }

    [Fact]
    public void An_explicit_medium_wins_over_the_implied_one()
    {
        var attribution = AttributionNormalizer.Normalize(new ResponseAttributionRequest("wa", "Story", null, null, null));

        Assert.Equal(("whatsapp", "story"), (attribution?.Source, attribution?.Medium));
    }

    [Fact]
    public void Blank_or_control_only_tags_yield_no_attribution() =>
        Assert.Null(AttributionNormalizer.Normalize(new ResponseAttributionRequest(" ", "", null, null, "\u0007")));

    [Fact]
    public void Tags_are_cut_to_a_hundred_characters() =>
        Assert.Equal(100, AttributionNormalizer.NormalizeTag(new string('a', 150))?.Length);
}

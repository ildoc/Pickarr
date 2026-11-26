using Pickarr.Core.Domain;
using Pickarr.Core.Metadata;
using Shouldly;

namespace Pickarr.Core.Tests;

public sealed class TmdbStandUpTests
{
    [Theory]
    [InlineData("stand-up comedy", true)]
    [InlineData("Stand Up Comedy", true)]
    [InlineData("standup comedy", true)]
    [InlineData("stand–up comedy", true)]
    [InlineData("standup", false)]
    [InlineData("superhero", false)]
    [InlineData("drama", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void Detects_stand_up_keyword_names(string? name, bool expected)
    {
        TmdbStandUp.IsStandUpKeyword(name).ShouldBe(expected);
    }

    [Fact]
    public void HasStandUpKeyword_matches_any_keyword_name()
    {
        TmdbStandUp.HasStandUpKeyword(["superhero", "based on comic"]).ShouldBeFalse();
        TmdbStandUp.HasStandUpKeyword(["live", "stand-up comedy"]).ShouldBeTrue();
        TmdbStandUp.HasStandUpKeyword(Array.Empty<string?>()).ShouldBeFalse();
    }

    [Fact]
    public void LanguageFallback_triggers_on_empty_overview_or_title()
    {
        LanguageFallback.NeedsEnglishFallback(null, "x").ShouldBeTrue();
        LanguageFallback.NeedsEnglishFallback("x", "").ShouldBeTrue();
        LanguageFallback.NeedsEnglishFallback("x", "y").ShouldBeFalse();
    }

    [Fact]
    public void TextNormalizer_strips_diacritics()
    {
        TextNormalizer.NormalizeName("Caffè").ShouldBe("caffe");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TextNormalizer_empty_inputs_become_empty_string(string? value)
    {
        TextNormalizer.NormalizeName(value).ShouldBe(string.Empty);
    }
}

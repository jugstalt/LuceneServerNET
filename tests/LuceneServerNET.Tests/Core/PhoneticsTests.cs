using LuceneServerNET.Core.Phonetics;

namespace LuceneServerNET.Tests.Core;

public class ColognePhoneticsTests
{
    [Theory]
    [InlineData("Müller-Lüdenscheidt", "65752682")]
    [InlineData("Wikipedia", "3412")]
    [InlineData("Meier", "67")]
    [InlineData("Mayer", "67")]
    [InlineData("Philipp", "351")]
    [InlineData("Christoph", "47823")]
    [InlineData("Novac", "638")]
    [InlineData("Bloc", "158")]
    [InlineData("Lusc", "58")]
    public void ToColognePhonetics_CleanZeros_MatchesReference(string input, string expected)
        => Assert.Equal(expected, input.ToColognePhonetics(cleanDoubles: true, cleanZeros: true));

    [Fact]
    public void ToColognePhonetics_KeepZeros_KeepsVowelCodes()
        => Assert.Equal("30401020", "Wikipedia".ToColognePhonetics(cleanDoubles: true, cleanZeros: false));

    [Fact]
    public void ToColognePhonetics_WithDoubles_KeepsRepeatedCodes()
        => Assert.Equal("605507", "Müller".ToColognePhonetics(cleanDoubles: false, cleanZeros: false));

    [Fact]
    public void ToColognePhonetics_IsCaseInsensitive()
        => Assert.Equal("Müller".ToColognePhonetics(), "MÜLLER".ToColognePhonetics());

    [Fact]
    public void ToColognePhonetics_Empty_ReturnsEmpty()
        => Assert.Equal("", "".ToColognePhonetics());

    [Fact]
    public void ToColognePhonetics_TrailingC_IsEncoded()
        => Assert.Equal("68", "Mac".ToColognePhonetics(cleanDoubles: true, cleanZeros: true));
}

public class SoundexTests
{
    [Theory]
    [InlineData("Robert", "R163")]
    [InlineData("Rupert", "R163")]
    [InlineData("Ashcraft", "A261")]
    [InlineData("Lee", "L000")]
    [InlineData("robert", "R163")]
    [InlineData("Tymczak", "T522")]
    [InlineData("Pfister", "P236")]
    [InlineData("Jackson", "J250")]
    [InlineData("Schmidt", "S530")]
    [InlineData("Honeyman", "H555")]
    public void ToSoundex_MatchesReference(string input, string expected)
        => Assert.Equal(expected, input.ToSoundex());

    [Fact]
    public void ToSoundex_Empty_IsPadded()
        => Assert.Equal("0000", "".ToSoundex());

    [Fact]
    public void ToSoundex_FirstLetterCode_IsNotRepeated()
        => Assert.Equal("P236", "Pfister".ToSoundex());

    [Fact]
    public void ToSoundex_VowelSeparatesSameCodes()
        => Assert.Equal("T522", "Tymczak".ToSoundex());

    [Fact]
    public void Difference_SameSoundex_Returns4()
        => Assert.Equal(4, "Smith".Difference("Smyth"));
}

public class PhoneticsStringExtensionsTests
{
    [Fact]
    public void ToPhonetics_Cologne_EncodesWordsAndKeepsNumbers()
        => Assert.Equal("068 10207 123", "Hans-Peter 123".ToPhonetics(Algorithm.ColognePhonetics));

    [Fact]
    public void ToPhonetics_None_ReturnsLowercaseWords()
        => Assert.Equal("hans peter", "Hans  Peter".ToPhonetics(Algorithm.None));

    [Fact]
    public void ToPhonetics_Soundex_KeepsWordsWithDigits()
        => Assert.Equal("R163 a1", "Robert a1".ToPhonetics(Algorithm.Soundex));

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void TermToLowercaseWords_Empty_ReturnsEmptyArray(string term)
        => Assert.Empty(term.TermToLowercaseWords());
}

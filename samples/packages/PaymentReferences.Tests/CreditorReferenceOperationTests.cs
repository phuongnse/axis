using PaymentReferences;

namespace PaymentReferences.Tests;

public sealed class CreditorReferenceOperationTests
{
    [Fact]
    public void Published_ISO_11649_example_gives_its_creditor_reference()
    {
        Assert.Equal("RF18539007547034", CreditorReferenceOperation.Create("539007547034"));
    }

    [Fact]
    public void Purchase_request_number_loses_its_hyphens_and_passes_the_mod_97_check()
    {
        var reference = CreditorReferenceOperation.Create("PR-2026-00012");

        Assert.Equal("PR202600012", reference[4..]);
        Assert.StartsWith("RF", reference, StringComparison.Ordinal);
        Assert.Equal(1, Mod97(reference[4..] + reference[..4]));
    }

    [Fact]
    public void Lower_case_letters_are_upper_cased()
    {
        Assert.Equal(CreditorReferenceOperation.Create("PR-2026-00012"), CreditorReferenceOperation.Create("pr-2026-00012"));
    }

    [Fact]
    public void Reference_without_a_letter_or_digit_is_refused()
    {
        Assert.Throws<ArgumentException>(() => CreditorReferenceOperation.Create(""));
        Assert.Throws<ArgumentException>(() => CreditorReferenceOperation.Create("--"));
    }

    [Fact]
    public void Reference_of_21_characters_is_accepted_and_one_of_22_is_refused()
    {
        Assert.Equal(25, CreditorReferenceOperation.Create(new string('1', 21)).Length);
        Assert.Throws<ArgumentException>(() => CreditorReferenceOperation.Create(new string('1', 22)));
    }

    [Fact]
    public async Task Operation_returns_the_creditor_reference_in_its_output()
    {
        var output = await new CreditorReferenceOperation().RunAsync(
            new Dictionary<string, object?> { ["reference"] = "539007547034" },
            null!,
            TestContext.Current.CancellationToken);

        Assert.Equal("RF18539007547034", Assert.Single(output).Value);
        Assert.Equal("creditorReference", Assert.Single(output).Key);
    }

    /// <summary>The ISO 7064 mod 97-10 check of a number whose letters count as 10 for A up to 35 for Z.</summary>
    private static int Mod97(string text)
    {
        var remainder = 0;
        foreach (var character in text)
        {
            var value = char.IsAsciiDigit(character) ? character - '0' : character - 'A' + 10;
            remainder = (value < 10 ? remainder * 10 + value : remainder * 100 + value) % 97;
        }

        return remainder;
    }
}

using System.Globalization;
using System.Text;
using Axis.Extensions;

namespace PaymentReferences;

/// <summary>
/// Turns a reference, such as a purchase request number, into an ISO 11649 creditor reference: <c>RF</c>,
/// two mod-97 check digits, then the reference without separators.
/// </summary>
public sealed class CreditorReferenceOperation : IOperation
{
    private const int MaxBodyLength = 21;

    public Task<IReadOnlyDictionary<string, object?>> RunAsync(
        IReadOnlyDictionary<string, object?> input,
        IOperationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        IReadOnlyDictionary<string, object?> output = new Dictionary<string, object?>
        {
            ["creditorReference"] = Create((string)input["reference"]!),
        };
        return Task.FromResult(output);
    }

    /// <summary>
    /// The creditor reference of <paramref name="reference"/>. Only its ASCII letters and digits count, in
    /// upper case, and at most 21 of them fit.
    /// </summary>
    /// <exception cref="ArgumentException">The reference has no letter or digit, or more than 21.</exception>
    public static string Create(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var body = new StringBuilder(reference.Length);
        foreach (var character in reference)
        {
            if (char.IsAsciiLetterOrDigit(character))
            {
                body.Append(char.ToUpperInvariant(character));
            }
        }

        if (body.Length is 0 or > MaxBodyLength)
        {
            throw new ArgumentException($"The reference must hold 1 to {MaxBodyLength} letters or digits, but '{reference}' holds {body.Length}.", nameof(reference));
        }

        var digits = body.ToString();
        var check = 98 - Mod97(digits + "RF00");
        return "RF" + check.ToString("D2", CultureInfo.InvariantCulture) + digits;
    }

    /// <summary>The remainder after dividing by 97 the number that <paramref name="text"/> stands for, with A as 10 up to Z as 35.</summary>
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

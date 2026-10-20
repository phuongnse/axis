using Axis.Expressions.Diagnostics;
using Axis.Expressions.Parsing;

namespace Axis.Expressions.Tests;

public sealed class ExpressionRangeTests
{
    [Fact]
    public void ResultTypeMismatchCoversTheWholeExpression()
    {
        const string Expression = "quantity + 1";
        var diagnostic = new ExpressionDiagnostic(ExpressionDiagnosticCodes.ResultTypeMismatch, "The result is not a boolean.", 9);

        Assert.Equal((0, Expression.Length), ExpressionRange.Of(Expression, diagnostic));
    }

    [Fact]
    public void TooLongCoversTheWholeExpression()
    {
        var expression = new string('1', ExpressionLimits.MaxLength + 5);
        var diagnostic = ExpressionParser.Parse(expression).Diagnostic!;

        Assert.Equal(ExpressionDiagnosticCodes.TooLong, diagnostic.Code);
        Assert.Equal((0, expression.Length), ExpressionRange.Of(expression, diagnostic));
    }

    [Fact]
    public void QualifiedCallCoversTheModuleAndTheName()
    {
        const string Expression = "not departments:IsManager(requester)";
        var diagnostic = new ExpressionDiagnostic(ExpressionDiagnosticCodes.UndeclaredModuleReference, "The module is not declared.", 4);

        Assert.Equal((4, 4 + "departments:IsManager".Length), ExpressionRange.Of(Expression, diagnostic));
    }

    [Fact]
    public void SyntaxErrorOnACharacterCoversThatCharacter()
    {
        const string Expression = "quantity # 1";
        var diagnostic = ExpressionParser.Parse(Expression).Diagnostic!;

        Assert.Equal(ExpressionDiagnosticCodes.SyntaxError, diagnostic.Code);
        Assert.Equal((9, 10), ExpressionRange.Of(Expression, diagnostic));
    }

    [Fact]
    public void UnclosedTextRunsToTheEnd()
    {
        const string Expression = "status == 'sub";
        var diagnostic = ExpressionParser.Parse(Expression).Diagnostic!;

        Assert.Equal((10, Expression.Length), ExpressionRange.Of(Expression, diagnostic));
    }

    [Theory]
    [InlineData("quantity > 'x'", 9, 9, 10)]
    [InlineData("quantity >= 10", 9, 9, 11)]
    [InlineData("status == 'draft'", 10, 10, 17)]
    [InlineData("total * rate", 6, 6, 7)]
    [InlineData("unknownName > 1", 0, 0, 11)]
    public void OtherDiagnosticsCoverTheTokenAtTheOffset(string expression, int offset, int start, int end)
    {
        var diagnostic = new ExpressionDiagnostic(ExpressionDiagnosticCodes.TypeMismatch, "A mismatch.", offset);

        Assert.Equal((start, end), ExpressionRange.Of(expression, diagnostic));
    }

    [Fact]
    public void EndOfTheExpressionIsAnEmptyRangeAtTheEnd()
    {
        const string Expression = "quantity >";
        var diagnostic = ExpressionParser.Parse(Expression).Diagnostic!;

        Assert.Equal((Expression.Length, Expression.Length), ExpressionRange.Of(Expression, diagnostic));
    }
}

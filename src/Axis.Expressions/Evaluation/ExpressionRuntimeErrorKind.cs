namespace Axis.Expressions.Evaluation;

/// <summary>Why an evaluation stopped without a value.</summary>
public enum ExpressionRuntimeErrorKind
{
    DivisionByZero,
    IntegerOverflow,

    /// <summary>A decimal result that <see cref="decimal"/> cannot hold exactly.</summary>
    DecimalOverflow,

    /// <summary>A <c>date</c> or <c>dateTime</c> literal whose text is not a valid date or instant.</summary>
    InvalidDateLiteral,

    /// <summary>An <c>addDays</c> result before 0001-01-01 or after 9999-12-31.</summary>
    DateOutOfRange,

    /// <summary>A function argument outside the range the function accepts, such as a <c>round</c> digit count above 28.</summary>
    ArgumentOutOfRange,

    /// <summary>The evaluation took more than <see cref="ExpressionLimits.MaxSteps"/> steps.</summary>
    StepBudgetExhausted,
}

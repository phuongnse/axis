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

    /// <summary>The evaluation took more than <see cref="ExpressionLimits.MaxSteps"/> steps.</summary>
    StepBudgetExhausted,
}

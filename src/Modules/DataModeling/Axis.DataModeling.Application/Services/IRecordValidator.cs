using Axis.DataModeling.Domain.Aggregates;
using FluentValidation.Results;

namespace Axis.DataModeling.Application.Services;

public interface IRecordValidator
{
    Task<List<ValidationFailure>> ValidateAsync(DataModel model, IReadOnlyDictionary<string, object?> data, CancellationToken ct = default);
}

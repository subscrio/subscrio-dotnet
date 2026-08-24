using FluentValidation.Results;

namespace Subscrio.Core.Application.Errors;

/// <summary>
/// Input validation error (FluentValidation validation failures)
/// </summary>
public class ValidationException : Exception
{
    public IReadOnlyList<ValidationFailure>? Errors { get; }

    public ValidationException(string message) : base(message)
    {
    }

    public ValidationException(string message, IReadOnlyList<ValidationFailure> errors) : base(message)
    {
        Errors = errors;
    }

    public ValidationException(string message, IReadOnlyList<ValidationFailure> errors, Exception innerException) : base(message, innerException)
    {
        Errors = errors;
    }
}



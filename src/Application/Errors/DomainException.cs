namespace Subscrio.Core.Application.Errors;

/// <summary>
/// Compatibility type for callers that import <c>Subscrio.Core.Application.Errors</c>.
/// Prefer <see cref="Domain.Errors.DomainException"/> in Domain code.
/// </summary>
public class DomainException : Domain.Errors.DomainException
{
    public DomainException(string message) : base(message)
    {
    }

    public DomainException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

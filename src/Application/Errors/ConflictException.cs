namespace Subscrio.Core.Application.Errors;

/// <summary>
/// Resource conflict error (duplicate key, etc.)
/// </summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }

    public ConflictException(string message, Exception innerException) : base(message, innerException)
    {
    }
}



using FluentValidation.Results;
using Subscrio.Core.Application.Errors;
using ValidationException = Subscrio.Core.Application.Errors.ValidationException;

namespace Subscrio.Core.Application.Utils;

/// <summary>
/// Shared guards for FluentValidation results and entity lookups.
/// </summary>
public static class ValidationGuard
{
    public static void EnsureValid(ValidationResult result, string message)
    {
        if (!result.IsValid)
        {
            throw new ValidationException(message, result.Errors);
        }
    }

    public static async Task EnsureKeyAvailableAsync<T>(
        Func<string, Task<T?>> findByKey,
        string key,
        string entityLabel) where T : class
    {
        var existing = await findByKey(key);
        if (existing != null)
        {
            throw new ConflictException($"{entityLabel} with key '{key}' already exists");
        }
    }

    public static async Task<T> RequireByKeyAsync<T>(
        Func<string, Task<T?>> findByKey,
        string key,
        string entityLabel) where T : class
    {
        var entity = await findByKey(key);
        if (entity == null)
        {
            throw new NotFoundException($"{entityLabel} with key '{key}' not found");
        }
        return entity;
    }
}

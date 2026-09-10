namespace Subscrio.Core.Domain.Constants;

/// <summary>
/// Domain-level constraints (kept out of Application so Domain stays independent).
/// </summary>
public static class DomainConstants
{
    public const int MaxDisplayNameLength = 255;
    public const int MinDisplayNameLength = 1;
}

namespace Subscrio.Core.Application.Constants;

/// <summary>
/// Application constants to avoid magic numbers and strings
/// </summary>
public static class ApplicationConstants
{
    // Display name constraints
    public const int MaxDisplayNameLength = 255;
    public const int MinDisplayNameLength = 1;

    // Key constraints
    public const int MaxKeyLength = 255;
    public const int MinKeyLength = 1;

    // Description constraints
    public const int MaxDescriptionLength = 1000;

    // Pagination defaults
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
    public const int MinPageSize = 1;

    // Search constraints
    public const int MaxSearchLength = 255;

    // Feature value constraints
    public const int MaxFeatureValueLength = 1000;

    // Subscription limits
    public const int MaxSubscriptionsPerCustomer = 100;

    // Cache settings
    public const int PlanCacheSize = 1000;

    // Performance settings
    public const int BatchSize = 50;
    public const int QueryTimeout = 60000; // 60 seconds
    public const int ConnectionTimeout = 30000; // 30 seconds
}



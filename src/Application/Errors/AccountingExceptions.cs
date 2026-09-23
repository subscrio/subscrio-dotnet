using Subscrio.Core.Application.DTOs;
namespace Subscrio.Core.Application.Errors;

public class UsageLimitExceededException(UsageDto entitlement) : Exception("Usage limit exceeded")
{
    public UsageDto Usage { get; } = entitlement;
}
public class InsufficientCreditsException(List<CreditCostDto> costs) : Exception("Insufficient credits")
{
    public List<CreditCostDto> Costs { get; } = costs;
}
public class IdempotencyConflictException() : ConflictException("Idempotency key was already used with a different request");
public class MeteringPeriodException(string message) : Exception(message);

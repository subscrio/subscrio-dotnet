using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Infrastructure.Database;
using Subscrio.Core.Infrastructure.Utils;

namespace Subscrio.Core.Application.Hooks;

public static class ApplySubscriptionDtoMutation
{
    public static void Apply(SubscriptionRecord record, SubscriptionDto dto, bool allowKeyChange = true)
    {
        if (dto.Key != record.Key)
        {
            if (!allowKeyChange)
            {
                throw new ValidationException("Subscription key cannot be changed via hooks");
            }
            record.Key = dto.Key;
        }

        record.IsArchived = dto.IsArchived;
        record.ActivationDate = ParseDate(dto.ActivationDate);
        record.ExpirationDate = ParseDate(dto.ExpirationDate);
        record.CancellationDate = ParseDate(dto.CancellationDate);
        record.TrialEndDate = ParseDate(dto.TrialEndDate);
        record.CurrentPeriodStart = ParseDate(dto.CurrentPeriodStart);
        record.CurrentPeriodEnd = ParseDate(dto.CurrentPeriodEnd);
        record.StripeSubscriptionId = dto.StripeSubscriptionId;
        record.Metadata = dto.Metadata;
        record.UpdatedAt = DateHelper.Now();
    }

    private static DateTime? ParseDate(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : DateTime.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);
}

using FluentAssertions;
using Stripe;
using Subscrio.Core;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Config;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Tests.Setup;
using Xunit;

namespace Subscrio.Core.Tests.E2E;

public class StripeIntegrationTests : IDisposable
{
    private readonly Subscrio _subscrio;
    private readonly TestFixtures _fixtures;

    public StripeIntegrationTests()
    {
        TestDatabaseAssemblyFixture.EnsureInitialized();

        var connectionString = TestDatabaseAssemblyFixture.GetTestConnectionString();
        var config = new SubscrioConfig
        {
            Database = new DatabaseConfig
            {
                ConnectionString = connectionString,
                Ssl = false,
                PoolSize = 5,
                DatabaseType = DatabaseType.PostgreSQL
            }
        };

        _subscrio = new Subscrio(config);
        _fixtures = new TestFixtures(_subscrio);
    }

    public void Dispose()
    {
        _subscrio?.Dispose();
    }

    public class SyntheticWebhookEvents : StripeIntegrationTests
    {
        public SyntheticWebhookEvents() : base() { }

        [Fact]
        public async Task ProcessStripeEvent_CreatesSubscription_FromCustomerSubscriptionCreated()
        {
            var (priceId, stripeCustomerId, stripeSubId, _) = await SeedStripeLinkedEntitiesAsync();

            var periodStart = DateTime.UtcNow;
            var periodEnd = periodStart.AddMonths(1);
            var stripeEvent = BuildSubscriptionEvent(
                EventTypes.CustomerSubscriptionCreated,
                BuildStripeSubscription(stripeSubId, stripeCustomerId, priceId, "active", periodStart, periodEnd)
            );

            await _subscrio.Stripe.ProcessStripeEventAsync(stripeEvent);

            var found = await _subscrio.Subscriptions.FindSubscriptionsAsync(new DetailedSubscriptionFilterDto(
                HasStripeId: true,
                Limit: 100
            ));
            found.Should().Contain(s => s.StripeSubscriptionId == stripeSubId);
            var created = found.Single(s => s.StripeSubscriptionId == stripeSubId);
            created.Status.Should().Be("active");
        }

        [Fact]
        public async Task ProcessStripeEvent_UpdatesSubscription_FromCustomerSubscriptionUpdated()
        {
            var (priceId, stripeCustomerId, stripeSubId, _) = await SeedStripeLinkedEntitiesAsync();

            var periodStart = DateTime.UtcNow;
            var periodEnd = periodStart.AddMonths(1);
            await _subscrio.Stripe.ProcessStripeEventAsync(BuildSubscriptionEvent(
                EventTypes.CustomerSubscriptionCreated,
                BuildStripeSubscription(stripeSubId, stripeCustomerId, priceId, "active", periodStart, periodEnd)
            ));

            var trialEnd = DateTime.UtcNow.AddDays(7);
            var updatedStripeSub = BuildStripeSubscription(
                stripeSubId,
                stripeCustomerId,
                priceId,
                "trialing",
                periodStart,
                periodEnd,
                trialEnd: trialEnd
            );
            await _subscrio.Stripe.ProcessStripeEventAsync(BuildSubscriptionEvent(
                EventTypes.CustomerSubscriptionUpdated,
                updatedStripeSub
            ));

            var found = await _subscrio.Subscriptions.FindSubscriptionsAsync(new DetailedSubscriptionFilterDto(
                HasStripeId: true,
                Limit: 100
            ));
            var updated = found.Single(s => s.StripeSubscriptionId == stripeSubId);
            updated.TrialEndDate.Should().NotBeNullOrEmpty();
            updated.Status.Should().Be("trial");
        }

        [Fact]
        public async Task ProcessStripeEvent_ArchivesOrCancels_FromCustomerSubscriptionDeleted()
        {
            var (priceId, stripeCustomerId, stripeSubId, _) = await SeedStripeLinkedEntitiesAsync();

            var periodStart = DateTime.UtcNow;
            var periodEnd = periodStart.AddMonths(1);
            await _subscrio.Stripe.ProcessStripeEventAsync(BuildSubscriptionEvent(
                EventTypes.CustomerSubscriptionCreated,
                BuildStripeSubscription(stripeSubId, stripeCustomerId, priceId, "active", periodStart, periodEnd)
            ));

            await _subscrio.Stripe.ProcessStripeEventAsync(BuildSubscriptionEvent(
                EventTypes.CustomerSubscriptionDeleted,
                BuildStripeSubscription(stripeSubId, stripeCustomerId, priceId, "canceled", periodStart, periodEnd)
            ));

            var found = await _subscrio.Subscriptions.FindSubscriptionsAsync(new DetailedSubscriptionFilterDto(
                HasStripeId: true,
                Limit: 100
            ));
            var deleted = found.Single(s => s.StripeSubscriptionId == stripeSubId);
            deleted.ExpirationDate.Should().NotBeNullOrEmpty();
            deleted.Status.Should().Be("expired");
        }

        private async Task<(string PriceId, string StripeCustomerId, string StripeSubId, string CustomerKey)> SeedStripeLinkedEntitiesAsync()
        {
            var product = await _fixtures.CreateProductAsync();
            var plan = await _fixtures.CreatePlanAsync(product.Key);
            var priceId = $"price_{Guid.NewGuid():N}";
            await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
            {
                ["ExternalProductId"] = priceId
            });
            var stripeCustomerId = $"cus_{Guid.NewGuid():N}";
            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["ExternalBillingId"] = stripeCustomerId
            });
            var stripeSubId = $"sub_{Guid.NewGuid():N}";
            return (priceId, stripeCustomerId, stripeSubId, customer.Key);
        }

        private static Subscription BuildStripeSubscription(
            string stripeSubId,
            string stripeCustomerId,
            string priceId,
            string status,
            DateTime periodStart,
            DateTime periodEnd,
            DateTime? trialEnd = null)
        {
            return new Subscription
            {
                Id = stripeSubId,
                CustomerId = stripeCustomerId,
                Status = status,
                Created = periodStart,
                CancelAtPeriodEnd = false,
                TrialEnd = trialEnd,
                Items = new StripeList<SubscriptionItem>
                {
                    Data = new List<SubscriptionItem>
                    {
                        new()
                        {
                            Id = $"si_{Guid.NewGuid():N}",
                            Price = new Price { Id = priceId },
                            CurrentPeriodStart = periodStart,
                            CurrentPeriodEnd = periodEnd
                        }
                    }
                }
            };
        }

        private static Event BuildSubscriptionEvent(string type, Subscription stripeSub)
        {
            return new Event
            {
                Id = $"evt_{Guid.NewGuid():N}",
                Type = type,
                Data = new EventData
                {
                    Object = stripeSub
                }
            };
        }
    }

    public class ObsoleteAndConfigGuards : StripeIntegrationTests
    {
        public ObsoleteAndConfigGuards() : base() { }

        [Fact]
#pragma warning disable CS0618
        public async Task CreateStripeSubscriptionAsync_ThrowsNotSupported()
        {
            var act = async () => await _subscrio.Stripe.CreateStripeSubscriptionAsync(
                "customer-key",
                "plan-key",
                "billing-cycle-key",
                "price_test"
            );

            await act.Should().ThrowAsync<NotSupportedException>();
        }
#pragma warning restore CS0618

        [Fact]
        public void ConstructStripeEvent_Throws_WhenWebhookSecretMissing()
        {
            var stripeConfig = new StripeConfig
            {
                SecretKey = "sk_test_no_webhook",
                WebhookSecret = null
            };

            Action act = () => stripeConfig.ConstructStripeEvent("{}", "t=1,v1=abc");
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*WebhookSecret*");
        }
    }
}

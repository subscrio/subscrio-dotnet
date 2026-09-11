using FluentAssertions;
using Stripe;
using Subscrio.Core;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Hooks;
using Subscrio.Core.Config;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Tests.Setup;
using Xunit;

namespace Subscrio.Core.Tests.E2E;

public class HooksTests : IDisposable
{
    private readonly Subscrio _subscrio;
    private readonly TestFixtures _fixtures;

    public HooksTests()
    {
        TestDatabaseAssemblyFixture.EnsureInitialized();
        var connectionString = TestDatabaseAssemblyFixture.GetTestConnectionString();
        _subscrio = new Subscrio(new SubscrioConfig
        {
            Database = new DatabaseConfig
            {
                ConnectionString = connectionString,
                Ssl = false,
                PoolSize = 5,
                DatabaseType = DatabaseType.PostgreSQL
            }
        });
        _fixtures = new TestFixtures(_subscrio);
    }

    public void Dispose()
    {
        _subscrio?.Dispose();
    }

    [Fact]
    public async Task CustomerCreated_EmitsBeforePersist_WithOldNull()
    {
        var events = new List<CustomerMutationHookEvent>();
        var off = _subscrio.Hooks.OnCustomerCreatedBefore(async (evt, _) =>
        {
            events.Add(evt);
            var existing = await _subscrio.Customers.GetCustomerAsync(evt.New!.Key);
            existing.Should().BeNull();
        });

        var key = $"hook-cust-{Guid.NewGuid():N}";
        await _subscrio.Customers.CreateCustomerAsync(new CreateCustomerDto(
            Key: key,
            DisplayName: "Hook Customer"
        ));

        events.Should().HaveCount(1);
        events[0].Source.Should().Be("api");
        events[0].Phase.Should().Be("before");
        events[0].Old.Should().BeNull();
        events[0].New!.Key.Should().Be(key);
        events[0].New!.DisplayName.Should().Be("Hook Customer");
        off();
    }

    [Fact]
    public async Task CustomerUpdated_AbortPreventsPersist()
    {
        var key = $"hook-upd-{Guid.NewGuid():N}";
        await _subscrio.Customers.CreateCustomerAsync(new CreateCustomerDto(
            Key: key,
            DisplayName: "Before"
        ));

        var events = new List<CustomerMutationHookEvent>();
        var off = _subscrio.Hooks.OnCustomerUpdatedBefore(async (evt, _) =>
        {
            events.Add(evt);
            throw new InvalidOperationException("abort update");
        });

        var act = async () => await _subscrio.Customers.UpdateCustomerAsync(
            key,
            new UpdateCustomerDto(DisplayName: "After"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("abort update");

        var persisted = await _subscrio.Customers.GetCustomerAsync(key);
        persisted!.DisplayName.Should().Be("Before");
        events.Should().HaveCount(1);
        events[0].Old!.DisplayName.Should().Be("Before");
        events[0].New!.DisplayName.Should().Be("After");
        off();
    }

    [Fact]
    public async Task CustomerArchivedAndDeleted_EmitOldNewJson()
    {
        var key = $"hook-arc-{Guid.NewGuid():N}";
        await _subscrio.Customers.CreateCustomerAsync(new CreateCustomerDto(
            Key: key,
            DisplayName: "Archive Me"
        ));

        var archived = new List<CustomerMutationHookEvent>();
        var deleted = new List<CustomerMutationHookEvent>();
        var off1 = _subscrio.Hooks.OnCustomerArchivedBefore(async (evt, _) => archived.Add(evt));
        var off2 = _subscrio.Hooks.OnCustomerDeletedBefore(async (evt, _) => deleted.Add(evt));

        await _subscrio.Customers.ArchiveCustomerAsync(key);
        archived.Should().HaveCount(1);
        archived[0].Old!.Status.Should().Be("active");
        archived[0].New!.Status.Should().Be("archived");
        archived[0].Source.Should().Be("api");

        await _subscrio.Customers.DeleteCustomerAsync(key);
        deleted.Should().HaveCount(1);
        deleted[0].Old!.Key.Should().Be(key);
        deleted[0].New.Should().BeNull();
        off1();
        off2();
    }

    [Fact]
    public async Task MultipleHandlers_ShareIdenticalPayload()
    {
        CustomerDto? first = null;
        CustomerDto? second = null;
        var off1 = _subscrio.Hooks.OnCustomerCreatedBefore(async (evt, _) => { first = evt.New; });
        var off2 = _subscrio.Hooks.OnCustomerCreatedBefore(async (evt, _) => { second = evt.New; });

        await _subscrio.Customers.CreateCustomerAsync(new CreateCustomerDto(
            Key: $"hook-multi-{Guid.NewGuid():N}",
            DisplayName: "Multi"
        ));

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        first.Should().BeEquivalentTo(second);
        off1();
        off2();
    }

    [Fact]
    public async Task HasListeners_False_WhenNoHandlers()
    {
        _subscrio.Hooks.HasListeners(HookEvents.CustomerDeletedBefore).Should().BeFalse();
    }

    [Fact]
    public async Task CustomerCreatedBefore_MutationAffectsPersistedData()
    {
        var key = $"hook-mut-{Guid.NewGuid():N}";
        var off = _subscrio.Hooks.OnCustomerCreatedBefore(async (evt, _) =>
        {
            evt.New!.DisplayName = "Mutated Name";
        });

        await _subscrio.Customers.CreateCustomerAsync(new CreateCustomerDto(
            Key: key,
            DisplayName: "Original Name"
        ));

        var persisted = await _subscrio.Customers.GetCustomerAsync(key);
        persisted!.DisplayName.Should().Be("Mutated Name");
        off();
    }

    [Fact]
    public async Task CustomerCreatedAfter_IncludesEntityId()
    {
        long? entityId = null;
        var off = _subscrio.Hooks.OnCustomerCreatedAfter(async (evt, _) =>
        {
            entityId = evt.EntityId;
        });

        var key = $"hook-eid-{Guid.NewGuid():N}";
        await _subscrio.Customers.CreateCustomerAsync(new CreateCustomerDto(
            Key: key,
            DisplayName: "Entity Id Customer"
        ));

        entityId.Should().NotBeNull();
        entityId!.Value.Should().BeGreaterThan(0);
        off();
    }

    [Fact]
    public async Task CustomerCreatedAfter_ThrowLeavesRow()
    {
        var key = $"hook-aft-{Guid.NewGuid():N}";
        var off = _subscrio.Hooks.OnCustomerCreatedAfter(async (evt, _) =>
        {
            throw new InvalidOperationException("after boom");
        });

        var act = async () => await _subscrio.Customers.CreateCustomerAsync(new CreateCustomerDto(
            Key: key,
            DisplayName: "After Throw"
        ));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("after boom");

        var persisted = await _subscrio.Customers.GetCustomerAsync(key);
        persisted.Should().NotBeNull();
        persisted!.DisplayName.Should().Be("After Throw");
        off();
    }

    [Fact]
    public async Task SubscriptionCreatedUpdatedDeleted_EmitWithApiSource()
    {
        var product = await _fixtures.CreateProductAsync();
        var plan = await _fixtures.CreatePlanAsync(product.Key);
        var cycle = await _fixtures.CreateBillingCycleAsync(plan.Key);
        var customer = await _fixtures.CreateCustomerAsync();

        var created = new List<SubscriptionMutationHookEvent>();
        var updated = new List<SubscriptionMutationHookEvent>();
        var deleted = new List<SubscriptionMutationHookEvent>();
        var off1 = _subscrio.Hooks.OnSubscriptionCreatedBefore(async (evt, _) => created.Add(evt));
        var off2 = _subscrio.Hooks.OnSubscriptionUpdatedBefore(async (evt, _) => updated.Add(evt));
        var off3 = _subscrio.Hooks.OnSubscriptionDeletedBefore(async (evt, _) => deleted.Add(evt));

        var subKey = $"hook-sub-{Guid.NewGuid():N}";
        await _subscrio.Subscriptions.CreateSubscriptionAsync(new CreateSubscriptionDto(
            Key: subKey,
            CustomerKey: customer.Key,
            BillingCycleKey: cycle.Key
        ));
        created.Should().HaveCount(1);
        created[0].Source.Should().Be("api");
        created[0].Phase.Should().Be("before");
        created[0].Old.Should().BeNull();
        created[0].New!.Key.Should().Be(subKey);

        await _subscrio.Subscriptions.UpdateSubscriptionAsync(
            subKey,
            new UpdateSubscriptionDto(Metadata: new Dictionary<string, object?> { ["note"] = "updated" }));
        updated.Should().HaveCount(1);
        updated[0].Old!.Key.Should().Be(subKey);
        updated[0].New!.Metadata.Should().ContainKey("note");

        await _subscrio.Subscriptions.DeleteSubscriptionAsync(subKey);
        deleted.Should().HaveCount(1);
        deleted[0].Old!.Key.Should().Be(subKey);
        deleted[0].New.Should().BeNull();

        off1();
        off2();
        off3();
    }

    [Fact]
    public async Task SubscriptionCreatedBefore_MutationAffectsPersistedData()
    {
        var product = await _fixtures.CreateProductAsync();
        var plan = await _fixtures.CreatePlanAsync(product.Key);
        var cycle = await _fixtures.CreateBillingCycleAsync(plan.Key);
        var customer = await _fixtures.CreateCustomerAsync();

        var off = _subscrio.Hooks.OnSubscriptionCreatedBefore(async (evt, _) =>
        {
            evt.New!.Metadata = new Dictionary<string, object?> { ["fromHook"] = "yes" };
        });

        var subKey = $"hook-sub-mut-{Guid.NewGuid():N}";
        await _subscrio.Subscriptions.CreateSubscriptionAsync(new CreateSubscriptionDto(
            Key: subKey,
            CustomerKey: customer.Key,
            BillingCycleKey: cycle.Key
        ));

        var persisted = await _subscrio.Subscriptions.GetSubscriptionAsync(subKey);
        persisted!.Metadata.Should().ContainKey("fromHook");
        persisted.Metadata!["fromHook"]!.ToString().Should().Be("yes");
        off();
    }

    [Fact]
    public async Task SubscriptionCreatedAfter_IncludesEntityId()
    {
        var product = await _fixtures.CreateProductAsync();
        var plan = await _fixtures.CreatePlanAsync(product.Key);
        var cycle = await _fixtures.CreateBillingCycleAsync(plan.Key);
        var customer = await _fixtures.CreateCustomerAsync();

        long? entityId = null;
        var off = _subscrio.Hooks.OnSubscriptionCreatedAfter(async (evt, _) =>
        {
            entityId = evt.EntityId;
        });

        var subKey = $"hook-sub-eid-{Guid.NewGuid():N}";
        await _subscrio.Subscriptions.CreateSubscriptionAsync(new CreateSubscriptionDto(
            Key: subKey,
            CustomerKey: customer.Key,
            BillingCycleKey: cycle.Key
        ));

        entityId.Should().NotBeNull();
        entityId!.Value.Should().BeGreaterThan(0);
        off();
    }

    [Fact]
    public async Task SubscriptionCreatedAfter_ThrowLeavesRow()
    {
        var product = await _fixtures.CreateProductAsync();
        var plan = await _fixtures.CreatePlanAsync(product.Key);
        var cycle = await _fixtures.CreateBillingCycleAsync(plan.Key);
        var customer = await _fixtures.CreateCustomerAsync();

        var off = _subscrio.Hooks.OnSubscriptionCreatedAfter(async (evt, _) =>
        {
            throw new InvalidOperationException("after sub boom");
        });

        var subKey = $"hook-sub-aft-{Guid.NewGuid():N}";
        var act = async () => await _subscrio.Subscriptions.CreateSubscriptionAsync(new CreateSubscriptionDto(
            Key: subKey,
            CustomerKey: customer.Key,
            BillingCycleKey: cycle.Key
        ));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("after sub boom");

        var persisted = await _subscrio.Subscriptions.GetSubscriptionAsync(subKey);
        persisted.Should().NotBeNull();
        persisted!.Key.Should().Be(subKey);
        off();
    }

    [Fact]
    public async Task FeatureOverrideAdded_EmitsBeforePersist()
    {
        var product = await _fixtures.CreateProductAsync();
        var feature = await _fixtures.CreateFeatureAsync();
        await _subscrio.Products.AssociateFeatureAsync(product.Key, feature.Key);
        var plan = await _fixtures.CreatePlanAsync(product.Key);
        var cycle = await _fixtures.CreateBillingCycleAsync(plan.Key);
        var customer = await _fixtures.CreateCustomerAsync();
        var sub = await _fixtures.CreateSubscriptionAsync(customer.Key, cycle.Key);

        var events = new List<SubscriptionMutationHookEvent>();
        var off = _subscrio.Hooks.OnSubscriptionFeatureOverrideAddedBefore(async (evt, _) => events.Add(evt));

        await _subscrio.Subscriptions.AddFeatureOverrideAsync(
            sub.Key,
            feature.Key,
            "true",
            OverrideType.Permanent);

        events.Should().HaveCount(1);
        events[0].FeatureKey.Should().Be(feature.Key);
        events[0].Value.Should().Be("true");
        events[0].OverrideType.Should().Be("permanent");
        events[0].Old!.Key.Should().Be(sub.Key);
        events[0].New!.Key.Should().Be(sub.Key);
        off();
    }

    [Fact]
    public async Task StripeReceived_FiresBeforeDomainHooks_WithStripeSource()
    {
        var product = await _fixtures.CreateProductAsync();
        var plan = await _fixtures.CreatePlanAsync(product.Key);
        var priceId = $"price_{Guid.NewGuid():N}";
        var cycle = await _fixtures.CreateBillingCycleAsync(plan.Key, new Dictionary<string, object>
        {
            ["ExternalProductId"] = priceId
        });
        var stripeCustomerId = $"cus_{Guid.NewGuid():N}";
        var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
        {
            ["ExternalBillingId"] = stripeCustomerId
        });

        var order = new List<string>();
        var stripeBefore = new List<StripeReceivedHookEvent>();
        var stripeAfter = new List<StripeReceivedHookEvent>();
        var subEvents = new List<SubscriptionMutationHookEvent>();
        var off1 = _subscrio.Hooks.OnStripeReceivedBefore(async (evt, _) =>
        {
            order.Add("stripe.received.before");
            stripeBefore.Add(evt);
        });
        var off2 = _subscrio.Hooks.OnSubscriptionCreatedBefore(async (evt, _) =>
        {
            order.Add("subscription.created.before");
            subEvents.Add(evt);
        });
        var off3 = _subscrio.Hooks.OnStripeReceivedAfter(async (evt, _) =>
        {
            order.Add("stripe.received.after");
            stripeAfter.Add(evt);
        });

        var periodStart = DateTime.UtcNow;
        var periodEnd = periodStart.AddMonths(1);
        var stripeSub = new Subscription
        {
            Id = $"sub_{Guid.NewGuid():N}",
            CustomerId = stripeCustomerId,
            Status = "active",
            Created = periodStart,
            CancelAtPeriodEnd = false,
            Metadata = new Dictionary<string, string>
            {
                ["subscrioCustomerKey"] = customer.Key
            },
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

        var stripeEvent = new Event
        {
            Id = $"evt_{Guid.NewGuid():N}",
            Type = EventTypes.CustomerSubscriptionCreated,
            Data = new EventData
            {
                Object = stripeSub
            }
        };

        await _subscrio.Stripe.ProcessStripeEventAsync(stripeEvent);

        order[0].Should().Be("stripe.received.before");
        order.Should().Contain("subscription.created.before");
        order[^1].Should().Be("stripe.received.after");
        order.IndexOf("stripe.received.before").Should().BeLessThan(order.IndexOf("subscription.created.before"));
        order.IndexOf("subscription.created.before").Should().BeLessThan(order.IndexOf("stripe.received.after"));
        stripeBefore[0].Phase.Should().Be("before");
        stripeBefore[0].Data.Id.Should().Be(stripeEvent.Id);
        stripeAfter[0].Phase.Should().Be("after");
        stripeAfter[0].Data.Id.Should().Be(stripeEvent.Id);
        subEvents.Should().Contain(e => e.Source == "stripe");
        off1();
        off2();
        off3();
    }

    [Fact]
    public async Task TransitionExpiredSubscriptions_EmitsSystemSourcedHooks()
    {
        var product = await _fixtures.CreateProductAsync();
        var paidPlan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
        {
            ["Key"] = $"paid-{Guid.NewGuid():N}"
        });
        var freePlan = await _fixtures.CreatePlanAsync(product.Key, new Dictionary<string, object>
        {
            ["Key"] = $"free-{Guid.NewGuid():N}"
        });
        var paidCycle = await _fixtures.CreateBillingCycleAsync(paidPlan.Key);
        var freeCycle = await _subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(
            PlanKey: freePlan.Key,
            Key: $"free-cycle-{Guid.NewGuid():N}",
            DisplayName: "Free Forever",
            DurationUnit: "forever",
            DurationValue: null
        ));

        await _subscrio.Plans.UpdatePlanAsync(paidPlan.Key, new UpdatePlanDto(
            OnExpireTransitionToBillingCycleKey: freeCycle.Key
        ));

        var customer = await _fixtures.CreateCustomerAsync();
        var subKey = $"trn-sub-{Guid.NewGuid():N}";
        await _fixtures.CreateSubscriptionAsync(customer.Key, paidCycle.Key, new Dictionary<string, object>
        {
            ["Key"] = subKey,
            ["ExpirationDate"] = DateTime.UtcNow.AddMinutes(-1)
        });

        var archived = new List<SubscriptionMutationHookEvent>();
        var created = new List<SubscriptionMutationHookEvent>();
        var off1 = _subscrio.Hooks.OnSubscriptionArchivedBefore(async (evt, _) => archived.Add(evt));
        var off2 = _subscrio.Hooks.OnSubscriptionCreatedBefore(async (evt, _) => created.Add(evt));

        var report = await _subscrio.Subscriptions.TransitionExpiredSubscriptionsAsync();
        report.Transitioned.Should().BeGreaterThanOrEqualTo(1);

        archived.Should().Contain(e => e.Source == "system" && e.Old!.Key == subKey);
        created.Should().Contain(e => e.Source == "system" && e.New!.Key.StartsWith(subKey));
        off1();
        off2();
    }

    [Fact]
    public async Task CustomerCreated_After_Fires()
    {
        var events = new List<CustomerMutationHookEvent>();
        var off = _subscrio.Hooks.OnCustomerCreatedAfter(async (evt, _) => events.Add(evt));

        var key = $"hook-cust-aft-{Guid.NewGuid():N}";
        await _subscrio.Customers.CreateCustomerAsync(new CreateCustomerDto(
            Key: key,
            DisplayName: "After Customer"
        ));

        events.Should().HaveCount(1);
        events[0].Phase.Should().Be("after");
        events[0].Source.Should().Be("api");
        events[0].New!.Key.Should().Be(key);
        events[0].Old.Should().BeNull();
        off();
    }

    [Fact]
    public async Task SubscriptionUpdated_After_Fires()
    {
        var product = await _fixtures.CreateProductAsync();
        var plan = await _fixtures.CreatePlanAsync(product.Key);
        var cycle = await _fixtures.CreateBillingCycleAsync(plan.Key);
        var customer = await _fixtures.CreateCustomerAsync();

        var subKey = $"hook-sub-upd-aft-{Guid.NewGuid():N}";
        await _subscrio.Subscriptions.CreateSubscriptionAsync(new CreateSubscriptionDto(
            Key: subKey,
            CustomerKey: customer.Key,
            BillingCycleKey: cycle.Key
        ));

        var events = new List<SubscriptionMutationHookEvent>();
        var off = _subscrio.Hooks.OnSubscriptionUpdatedAfter(async (evt, _) => events.Add(evt));

        await _subscrio.Subscriptions.UpdateSubscriptionAsync(
            subKey,
            new UpdateSubscriptionDto(Metadata: new Dictionary<string, object?> { ["note"] = "after" }));

        events.Should().HaveCount(1);
        events[0].Phase.Should().Be("after");
        events[0].Source.Should().Be("api");
        events[0].Old!.Key.Should().Be(subKey);
        events[0].New!.Metadata.Should().ContainKey("note");
        off();
    }

    [Fact]
    public async Task Unsubscribe_Off_PreventsFurtherEvents()
    {
        var events = new List<CustomerMutationHookEvent>();
        var off = _subscrio.Hooks.OnCustomerCreatedBefore(async (evt, _) => events.Add(evt));

        await _subscrio.Customers.CreateCustomerAsync(new CreateCustomerDto(
            Key: $"hook-off-1-{Guid.NewGuid():N}",
            DisplayName: "First"
        ));
        events.Should().HaveCount(1);

        off();

        await _subscrio.Customers.CreateCustomerAsync(new CreateCustomerDto(
            Key: $"hook-off-2-{Guid.NewGuid():N}",
            DisplayName: "Second"
        ));
        events.Should().HaveCount(1);
    }
}

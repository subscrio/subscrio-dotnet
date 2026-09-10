# Subscrio .NET Core Library

<p align="center">
  <a href="https://subscrio.com/dotnet-entitlement-library/">
    <img src="https://subscrio.com/assets/images/logo/logo-576x110.png" alt="Subscrio" width="220">
  </a>
</p>

<p align="center">
  <strong>The entitlement engine that translates subscriptions into feature access.</strong>
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/Subscrio.Core"><img src="https://img.shields.io/nuget/v/Subscrio.Core?style=flat-square&logo=nuget" alt="NuGet version"></a>
  <a href="https://github.com/subscrio/subscrio-dotnet/blob/main/LICENSE"><img src="https://img.shields.io/badge/License-MIT-yellow?style=flat-square" alt="MIT License"></a>
  <img src="https://img.shields.io/badge/.NET-8%20|%209%20|%2010-512BD4?style=flat-square&logo=dotnet" alt=".NET">
  <img src="https://img.shields.io/badge/PostgreSQL-4169E1?style=flat-square&logo=postgresql&logoColor=white" alt="PostgreSQL">
  <img src="https://img.shields.io/badge/SQL%20Server-CC2927?style=flat-square&logo=microsoftsqlserver&logoColor=white" alt="SQL Server">
</p>

<p align="center">
  <a href="https://subscrio.com"><img src="https://img.shields.io/badge/Website-subscrio.com-696dc0?style=flat-square" alt="Website"></a>
  <a href="https://docs.subscrio.com"><img src="https://img.shields.io/badge/Docs-docs.subscrio.com-696dc0?style=flat-square" alt="Documentation"></a>
  <a href="https://github.com/subscrio/subscrio"><img src="https://img.shields.io/badge/Hub-subscrio%2Fsubscrio-181717?style=flat-square&logo=github" alt="Hub"></a>
  <a href="https://github.com/subscrio/subscrio-dotnet/issues"><img src="https://img.shields.io/badge/Issues-report-181717?style=flat-square&logo=github" alt="Issues"></a>
</p>

An open-source .NET entitlement library for plan-based feature access, limits, subscriptions, customer overrides, and optional Stripe event processing.

See the [Subscrio hub README](https://github.com/subscrio/subscrio) for concepts, architecture, and feature resolution.

## Features

- Feature entitlements — toggles, numeric limits, text values, and customer overrides
- Plans and billing cycles — model packages and subscription timing without processing payments
- Subscription lifecycle — trials, renewals, cancellations, and effective access dates
- Stripe integration — process supported, verified Stripe subscription events
- PostgreSQL or SQL Server — Entity Framework Core with a multi-targeted NuGet package
- Hooks — before/after events for customers, subscriptions, and inbound Stripe payloads

## Installation

```bash
dotnet add package Subscrio.Core
```

Or add a package reference:

```xml
<PackageReference Include="Subscrio.Core" Version="0.3.1" />
```

**Prerequisites**

- .NET 8, 9, or 10
- PostgreSQL or SQL Server (create an empty database first; Subscrio installs schema inside it)

## Quick Start

Set `DATABASE_URL`, then wire up Subscrio and run the schema installer once:

```csharp
using Subscrio.Core;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Config;

var config = ConfigLoader.LoadConfig();
await using var subscrio = new Subscrio(config);

await subscrio.InstallSchemaAsync("your-admin-passphrase");

var product = await subscrio.Products.CreateProductAsync(new CreateProductDto(
    Key: "my-saas",
    DisplayName: "My SaaS Product"
));

var feature = await subscrio.Features.CreateFeatureAsync(new CreateFeatureDto(
    Key: "max-users",
    DisplayName: "Maximum Users",
    ValueType: "numeric",
    DefaultValue: "10"
));

await subscrio.Products.AssociateFeatureAsync(product.Key, feature.Key);

var plan = await subscrio.Plans.CreatePlanAsync(new CreatePlanDto(
    ProductKey: product.Key,
    Key: "pro-plan",
    DisplayName: "Pro Plan"
));

await subscrio.Plans.SetFeatureValueAsync(plan.Key, feature.Key, "100");

var billingCycle = await subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(
    PlanKey: plan.Key,
    Key: "monthly",
    DisplayName: "Monthly",
    DurationValue: 1,
    DurationUnit: "months"
));

var customer = await subscrio.Customers.CreateCustomerAsync(new CreateCustomerDto(
    Key: "customer-123",
    DisplayName: "Acme Corp"
));

await subscrio.Subscriptions.CreateSubscriptionAsync(new CreateSubscriptionDto(
    Key: "sub-001",
    CustomerKey: customer.Key,
    BillingCycleKey: billingCycle.Key
));

var maxUsers = await subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
    customer.Key,
    product.Key,
    "max-users"
);
```

Public APIs use string **keys**, not internal IDs. DTOs live in `Subscrio.Core.Application.DTOs`.

## Configuration

**Environment variables** (read by `ConfigLoader.LoadConfig()`):

| Variable | Required | Description |
|----------|----------|-------------|
| `DATABASE_URL` | Yes | PostgreSQL or SQL Server connection string |
| `DATABASE_TYPE` | No | `postgresql`, `postgres`, or `sqlserver` (default: PostgreSQL) |
| `DATABASE_SSL` | No | `true` to enable SSL |
| `DATABASE_POOL_SIZE` | No | Connection pool size (default: 10) |
| `STRIPE_SECRET_KEY` | No | Stripe secret key for billing helpers |
| `STRIPE_WEBHOOK_SECRET` | No | Stripe webhook endpoint secret (`whsec_...`) for `ConstructStripeEvent` |
| `ADMIN_PASSPHRASE` | No | Default admin passphrase for schema install |
| `LOG_LEVEL` | No | `debug`, `info`, `warn`, or `error` |

**Connection string examples**

- PostgreSQL: `Host=localhost;Port=5432;Database=subscrio;Username=postgres;Password=yourpassword`
- SQL Server: `Server=localhost;Database=Subscrio;User Id=sa;Password=yourpassword;TrustServerCertificate=true`

You can also build `SubscrioConfig` in code (from appsettings, a vault, etc.) and pass it to `new Subscrio(config)`.

## Database setup

1. Create an empty database on PostgreSQL or SQL Server.
2. Point Subscrio at it with `DATABASE_URL` or `SubscrioConfig.Database`.
3. On first run, call `InstallSchemaAsync(adminPassphrase)`.
4. After upgrading the package, call `MigrateAsync()`.

```csharp
var version = await subscrio.VerifySchemaAsync();
if (version == null)
{
    await subscrio.InstallSchemaAsync("your-admin-passphrase");
}

await subscrio.MigrateAsync();
```

Other instance methods: `DropSchemaAsync()` (destructive; tests/dev only) and `RunInitialConfigSyncAsync()` (when `SubscrioConfig.InitialConfig` is set). `AddSubscrio()` does **not** auto-install schema or run initial config sync — call those explicitly after building the host.

## Dependency injection

For ASP.NET Core apps, register Subscrio as **scoped** and inject it where needed:

```csharp
using Subscrio.Core;
using Subscrio.Core.Config;
using Subscrio.Core.DependencyInjection;

var config = ConfigLoader.LoadConfig(); // or build from IConfiguration
builder.Services.AddSubscrio(config, ServiceLifetime.Scoped);

var app = builder.Build();

// If InitialConfig is set, install schema (when needed) then sync explicitly — do not rely on AddSubscrio
using (var scope = app.Services.CreateScope())
{
    var subscrio = scope.ServiceProvider.GetRequiredService<Subscrio>();
    if (await subscrio.VerifySchemaAsync() == null)
        await subscrio.InstallSchemaAsync();
    await subscrio.RunInitialConfigSyncAsync();
}

// Inject Subscrio in controllers, services, or minimal API handlers
```

Use `Transient` for console or worker apps that create their own scope per operation. Avoid `Singleton` in web apps.

## Stripe

When `StripeConfig.WebhookSecret` is set (or `STRIPE_WEBHOOK_SECRET`), verify inbound webhooks with `ConstructStripeEvent`, then pass the event to `ProcessStripeEventAsync`. Without a webhook secret, verify signatures in your app before calling `ProcessStripeEventAsync`.

To let a customer subscribe via Stripe Checkout, use `CreateCheckoutSessionAsync` (the customer does not need `ExternalBillingId` beforehand — Checkout can create the Stripe customer). Sync resulting subscription changes through webhooks with `ProcessStripeEventAsync`.

```csharp
// Prefer: verify via Subscrio when WebhookSecret is configured
var stripeEvent = config.Stripe!.ConstructStripeEvent(json, signatureHeader);
await subscrio.Stripe.ProcessStripeEventAsync(stripeEvent);

var (url, sessionId) = await subscrio.Stripe.CreateCheckoutSessionAsync(
    customerKey: customer.Key,
    billingCycleKey: billingCycle.Key,
    successUrl: "https://example.com/success",
    cancelUrl: "https://example.com/cancel"
);
```

See [Stripe integration](https://docs.subscrio.com/reference/stripe-integration) and the [hub overview](https://github.com/subscrio/subscrio#stripe-integration).

## Documentation

Full API reference, hooks, and extension guides live on [docs.subscrio.com](https://docs.subscrio.com):

- [Core overview](https://docs.subscrio.com/reference/core-overview)
- [Feature checker & resolution](https://docs.subscrio.com/reference/feature-checker)
- [Hooks](https://docs.subscrio.com/reference/hooks)
- [How to extend](https://docs.subscrio.com/reference/how-to-extend)

**Services on `Subscrio`:** `Products`, `Features`, `Plans`, `BillingCycles`, `Customers`, `Subscriptions`, `FeatureChecker`, `ConfigSync`, `Stripe`, `Hooks`.

Handle `ValidationException`, `NotFoundException`, and `ConflictException` from `Subscrio.Core.Application.Errors`.

## Building and testing

From the directory that contains `Subscrio.Core.sln`:

```bash
dotnet build
dotnet test
```

Tests require PostgreSQL. Set `TEST_DATABASE_URL` or configure `tests/appsettings.json` — see [tests/README.md](tests/README.md). Extension packages (`Subscrio.AuditLog`, `Subscrio.Payments`) are separate repos with their own test suites.

## License

MIT — see [LICENSE](LICENSE).

## Contributing

Issues and pull requests welcome in this repo. Org-wide guidelines: [CONTRIBUTING](https://github.com/subscrio/subscrio/blob/main/CONTRIBUTING.md).

## Support

- [Subscrio hub](https://github.com/subscrio/subscrio)
- [Report issues](https://github.com/subscrio/subscrio-dotnet/issues)
- [Discussions](https://github.com/subscrio/subscrio/discussions) (org-wide)
- [Testing guide](tests/README.md)

<p align="center">
  Maintained by <a href="https://github.com/jasenf">Jasen Fici</a> · Part of the <a href="https://github.com/subscrio">Subscrio</a> org
</p>

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

See the [Subscrio hub README](https://github.com/subscrio/subscrio) for cross-cutting concepts and architecture.

## Installation

Add the NuGet package to your project:

```xml
<PackageReference Include="Subscrio.Core" Version="1.0.4" />
```

Or using the .NET CLI:

```bash
dotnet add package Subscrio.Core
```

**Prerequisites:**
- .NET 8.0 (LTS - supported until November 2026), .NET 9.0, or .NET 10.0
- PostgreSQL or SQL Server database

The library is multi-targeted: one NuGet package includes builds for all supported frameworks; the correct one is chosen by your project's target framework.

## Database and connection

### Creating the database

You need a database before using Subscrio. The library does not create the database itself; it only installs and updates schema inside an existing database.

**PostgreSQL**

- Create a database (e.g. `subscrio`) using `psql`, pgAdmin, or your host’s tooling.
- Example with `psql`:
  ```bash
  psql -U postgres -c "CREATE DATABASE subscrio;"
  ```
- Ensure the user in your connection string has rights to create tables and run migrations in that database.

**SQL Server**

- Create a database (e.g. `Subscrio`) in SQL Server Management Studio or with T-SQL:
  ```sql
  CREATE DATABASE Subscrio;
  ```
- The login in your connection string must have permission to create tables and run migrations in that database.

### Setting the connection string

Set the connection string in one of these ways:

1. **Environment variable (recommended for local and production)**  
   Set `DATABASE_URL`:
   - **PostgreSQL:** `Host=localhost;Port=5432;Database=subscrio;Username=postgres;Password=yourpassword`
   - **SQL Server:** `Server=localhost;Database=Subscrio;User Id=sa;Password=yourpassword;TrustServerCertificate=true`

2. **`ConfigLoader.LoadConfig()`**
   `ConfigLoader.LoadConfig()` reads `DATABASE_URL` (and optionally `DATABASE_TYPE`, `STRIPE_SECRET_KEY`) from the process environment. Use this when you configure the app via env vars or launchSettings.

3. **Explicit config in code**  
   Build a `SubscrioConfig` and set `Database.ConnectionString` and `Database.DatabaseType` (e.g. `DatabaseType.PostgreSQL` or `DatabaseType.SqlServer`). Use this for custom config sources (e.g. key vault, appsettings).

4. **ASP.NET Core appsettings**  
   You can load connection strings from `appsettings.json` or other configuration and pass them into the constructor or into the object you use to build `SubscrioConfig`.

After the database exists and the connection string is set, use **Database setup and migrations** below to install or update the schema.

## Building and testing

From the directory that contains `Subscrio.Core.sln` (this repository's root):

```bash
dotnet build
dotnet test
```

`dotnet test` runs `Subscrio.Core.Tests` against PostgreSQL. Set `TEST_DATABASE_URL` or configure `tests/appsettings.json` (see [tests/README.md](tests/README.md)). Extension packages (`Subscrio.AuditLog`, `Subscrio.Payments`) are separate repositories with their own test suites.

## Quick Start

```csharp
using Subscrio.Core;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Config;

// Load configuration
var config = ConfigLoader.LoadConfig();

// Initialize the library
var subscrio = new Subscrio(config);

// Install database schema (first time only)
await subscrio.InstallSchemaAsync("your-admin-passphrase");

// Create a product
var product = await subscrio.Products.CreateProductAsync(new CreateProductDto(
    Key: "my-saas",
    DisplayName: "My SaaS Product"
));

// Create a feature
var feature = await subscrio.Features.CreateFeatureAsync(new CreateFeatureDto(
    Key: "max-users",
    DisplayName: "Maximum Users",
    ValueType: "numeric",
    DefaultValue: "10"
));

// Associate feature with product (using keys, not IDs)
await subscrio.Products.AssociateFeatureAsync(product.Key, feature.Key);

// Create a plan (using productKey, not productId)
var plan = await subscrio.Plans.CreatePlanAsync(new CreatePlanDto(
    ProductKey: product.Key,
    Key: "pro-plan",
    DisplayName: "Pro Plan"
));

// Set feature value on plan (using keys, not IDs)
await subscrio.Plans.SetFeatureValueAsync(plan.Key, feature.Key, "100");

// Create a billing cycle for the plan (required for subscriptions)
var billingCycle = await subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(
    PlanKey: plan.Key,
    Key: "monthly",
    DisplayName: "Monthly",
    DurationValue: 1,
    DurationUnit: "months"
));

// Create a customer (using key, not externalId)
var customer = await subscrio.Customers.CreateCustomerAsync(new CreateCustomerDto(
    Key: "customer-123",
    DisplayName: "Acme Corp"
));

// Create a subscription (using keys and billingCycleKey, not IDs)
var subscription = await subscrio.Subscriptions.CreateSubscriptionAsync(new CreateSubscriptionDto(
    Key: "sub-001",
    CustomerKey: customer.Key,
    BillingCycleKey: billingCycle.Key
));

// Check feature access (requires customerKey, productKey, and featureKey)
var maxUsers = await subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
    customer.Key,
    product.Key,
    "max-users"
);
Console.WriteLine($"Customer can have {maxUsers} users"); // "100"
```

## Dependency Injection

Subscrio supports dependency injection and can be registered in your DI container. **This is the recommended approach for web applications** and provides better lifetime management.

### Quick setup (when you want to use DI)

1. **Add the using** for the extension: `using Subscrio.Core.DependencyInjection;`  
   (You may also need `using Microsoft.Extensions.DependencyInjection;` for `ServiceLifetime` if not in scope.)

2. **Register Subscrio** in your host builder (e.g. in `Program.cs`):
   ```csharp
   var config = ConfigLoader.LoadConfig(); // or build SubscrioConfig from appsettings/options
   builder.Services.AddSubscrio(config, ServiceLifetime.Scoped);
   ```

3. **Inject `Subscrio`** where you need it: constructor injection in controllers or services, or as a parameter in minimal API handlers (e.g. `app.MapGet("/products", async (Subscrio subscrio) => ...)`).

**Config from appsettings (optional):** You can build `SubscrioConfig` from your own config (e.g. `builder.Configuration` or `IOptions`) and pass it to `AddSubscrio`. The examples below use `ConfigLoader.LoadConfig()` for simplicity.

See the examples below for full ASP.NET Core, console, and controller usage.

### Registration

#### ASP.NET Core Web API

```csharp
using Subscrio.Core;
using Subscrio.Core.Config;
using Subscrio.Core.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Load configuration
var config = ConfigLoader.LoadConfig();

// Register Subscrio with Scoped lifetime (recommended for web apps)
builder.Services.AddSubscrio(config, ServiceLifetime.Scoped);

var app = builder.Build();

// Use in controllers
app.MapGet("/products", async (Subscrio subscrio) =>
{
    var products = await subscrio.Products.ListProductsAsync();
    return Results.Ok(products);
});

app.Run();
```

#### Console Application with DI

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Subscrio.Core;
using Subscrio.Core.Config;
using Subscrio.Core.DependencyInjection;

var config = ConfigLoader.LoadConfig();

var services = new ServiceCollection();
services.AddSubscrio(config, ServiceLifetime.Scoped);

var serviceProvider = services.BuildServiceProvider();

// Use scoped service
using var scope = serviceProvider.CreateScope();
var subscrio = scope.ServiceProvider.GetRequiredService<Subscrio>();

var products = await subscrio.Products.ListProductsAsync();
```

#### Controller Injection

```csharp
using Microsoft.AspNetCore.Mvc;
using Subscrio.Core;
using Subscrio.Core.Application.DTOs;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly Subscrio _subscrio;

    public ProductsController(Subscrio subscrio)
    {
        _subscrio = subscrio; // Injected by DI
    }

    [HttpGet]
    public async Task<ActionResult<List<ProductDto>>> GetProducts()
    {
        var products = await _subscrio.Products.ListProductsAsync();
        return Ok(products);
    }

    [HttpGet("{key}")]
    public async Task<ActionResult<ProductDto>> GetProduct(string key)
    {
        var product = await _subscrio.Products.GetProductAsync(key);
        if (product == null)
        {
            return NotFound();
        }

        return Ok(product);
    }

    [HttpPost]
    public async Task<ActionResult<ProductDto>> CreateProduct(CreateProductDto dto)
    {
        var product = await _subscrio.Products.CreateProductAsync(dto);
        return CreatedAtAction(nameof(GetProduct), new { key = product.Key }, product);
    }
}
```

### Service Lifetime Options

The `AddSubscrio()` method accepts a `ServiceLifetime` parameter:

#### Scoped (Recommended for Web Applications)

```csharp
services.AddSubscrio(config, ServiceLifetime.Scoped);
```

- **When to use**: ASP.NET Core web applications
- **Behavior**: One `Subscrio` instance per HTTP request
- **Benefits**: 
  - Each request gets a fresh `Subscrio` instance
  - Each instance creates its own `DbContext`
  - Change tracker is clean for each request
  - No tracking conflicts between requests
  - Proper resource cleanup after request completes

#### Transient

```csharp
services.AddSubscrio(config, ServiceLifetime.Transient);
```

- **When to use**: Console applications, background services, or when you need a new instance each time
- **Behavior**: New `Subscrio` instance every time it's requested
- **Benefits**: Maximum isolation, each operation gets a fresh instance

#### Singleton

```csharp
services.AddSubscrio(config, ServiceLifetime.Singleton);
```

- **When to use**: Rarely recommended - only if you want a single instance for the entire application lifetime
- **Behavior**: One `Subscrio` instance shared across the entire application
- **Warning**: Can cause EF Core tracking conflicts if multiple operations use the same instance
- **Not recommended** for web applications

### How Subscrio Creates DbContext

Subscrio creates its own `DbContext` internally when instantiated. This means:

- **With Scoped lifetime**: Each scope (HTTP request) gets a new `Subscrio` → new `DbContext` → clean change tracker
- **With Transient lifetime**: Each request gets a new `Subscrio` → new `DbContext` → clean change tracker
- **With Singleton lifetime**: Single `Subscrio` → single `DbContext` → potential tracking conflicts

**Best Practice**: Use `ServiceLifetime.Scoped` for web applications to ensure proper isolation and resource management.

## API Reference

### Core Services

- **`subscrio.Products`** - Product management
- **`subscrio.Features`** - Feature entitlement definitions
- **`subscrio.Plans`** - Subscription plan management
- **`subscrio.BillingCycles`** - Billing cycle management
- **`subscrio.Customers`** - Customer management
- **`subscrio.Subscriptions`** - Subscription lifecycle
- **`subscrio.FeatureChecker`** - Feature access checking
- **`subscrio.Stripe`** - Stripe integration
- **`subscrio.Hooks`** - Before/after hooks for customers, subscriptions, and inbound Stripe events (`*.Before` / `*.After`). Full TypeScript and .NET examples (tabbed) live in the [docs repository](https://github.com/subscrio/docs/blob/main/docs/reference/hooks.md) and [how-to-extend guide](https://github.com/subscrio/docs/blob/main/docs/reference/how-to-extend.md).

```csharp
subscrio.Hooks.OnCustomerUpdatedBefore(async (evt, ct) =>
{
    await audit.WriteAsync(evt.Old, evt.New, evt.Source, ct);
});

subscrio.Hooks.OnCustomerCreatedAfter(async (evt, ct) =>
{
    await notify.ReadyAsync(evt.EntityId, evt.New, ct);
});
```

### Instance Methods

- **`InstallSchemaAsync(adminPassphrase)`** - Install database schema
- **`VerifySchemaAsync()`** - Check if schema is installed (returns version string or null)
- **`MigrateAsync()`** - Run database migrations
- **`DropSchemaAsync()`** - Drop all Subscrio tables (destructive; tests and local dev only)
- **`RunInitialConfigSyncAsync()`** - Apply `InitialConfigSync` from `SubscrioConfig` when configured
- **`Dispose()`** - Clean up resources (implements IDisposable)

## Configuration

### Configuration Structure

```csharp
var config = new SubscrioConfig
{
    Database = new DatabaseConfig
    {
        ConnectionString = "Host=localhost;Port=5432;Database=subscrio;Username=postgres;Password=password",
        DatabaseType = DatabaseType.PostgreSQL, // or DatabaseType.SqlServer
        Ssl = false,
        PoolSize = 10
    },
    Stripe = new StripeConfig
    {
        SecretKey = "sk_test_..." // Optional
    }
};
```

### Loading Configuration

#### From Environment Variables

```csharp
var config = ConfigLoader.LoadConfig();
```

This reads from:
- `DATABASE_URL` - Database connection string
- `DATABASE_TYPE` - `postgresql`, `postgres`, or `sqlserver` (case-insensitive)
- `STRIPE_SECRET_KEY` - Stripe secret key (optional)

#### From Custom Source

```csharp
var config = new SubscrioConfig
{
    Database = new DatabaseConfig
    {
        ConnectionString = "your-connection-string",
        DatabaseType = DatabaseType.PostgreSQL
    }
};
```

## Database setup and migrations

Summary of how to get the database ready and keep it up to date:

| Step | When | What to do |
|------|------|------------|
| 1. Create DB | Once | Create an empty PostgreSQL or SQL Server database (see [Database and connection](#database-and-connection)). |
| 2. Connection string | Once | Set `DATABASE_URL` (or pass `SubscrioConfig` with `Database.ConnectionString`). |
| 3. Install schema | First run only | Call `InstallSchemaAsync(adminPassphrase)` to create all tables and seed the admin passphrase. |
| 4. Migrate | After library updates | Call `MigrateAsync()` to apply any new migrations. |

### First-time schema installation

```csharp
var subscrio = new Subscrio(config);
await subscrio.InstallSchemaAsync("your-secure-admin-passphrase");
```

This creates every required table and stores the hashed admin passphrase. Run it once per database.

### Checking if schema is installed

```csharp
var version = await subscrio.VerifySchemaAsync();
if (version == null)
{
    await subscrio.InstallSchemaAsync("your-admin-passphrase");
}
```

Use this on startup if you want to install the schema only when it is missing.

### Running migrations

After upgrading the Subscrio library, run migrations so the database schema stays in sync:

```csharp
var migrationsApplied = await subscrio.MigrateAsync();
Console.WriteLine($"Applied {migrationsApplied} migrations");
```

Call `MigrateAsync()` during startup or as part of your deployment; it is safe to call when there are no pending migrations (it will apply zero and return 0).

## Usage Examples

```csharp
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Domain.ValueObjects;
```

### Create a Product with Features

```csharp
// Create product
var product = await subscrio.Products.CreateProductAsync(new CreateProductDto(
    Key: "saas-platform",
    DisplayName: "SaaS Platform"
));

// Create features
var maxProjects = await subscrio.Features.CreateFeatureAsync(new CreateFeatureDto(
    Key: "max-projects",
    DisplayName: "Max Projects",
    ValueType: "numeric",
    DefaultValue: "10"
));

var ganttCharts = await subscrio.Features.CreateFeatureAsync(new CreateFeatureDto(
    Key: "gantt-charts",
    DisplayName: "Gantt Charts",
    ValueType: "toggle",
    DefaultValue: "false"
));

// Associate features with product
await subscrio.Products.AssociateFeatureAsync(product.Key, maxProjects.Key);
await subscrio.Products.AssociateFeatureAsync(product.Key, ganttCharts.Key);
```

### Create Plans with Feature Values

```csharp
// Create plan
var basicPlan = await subscrio.Plans.CreatePlanAsync(new CreatePlanDto(
    ProductKey: product.Key,
    Key: "basic",
    DisplayName: "Basic Plan"
));

var proPlan = await subscrio.Plans.CreatePlanAsync(new CreatePlanDto(
    ProductKey: product.Key,
    Key: "pro",
    DisplayName: "Pro Plan"
));

// Set feature values for plans
await subscrio.Plans.SetFeatureValueAsync(proPlan.Key, maxProjects.Key, "50");
await subscrio.Plans.SetFeatureValueAsync(proPlan.Key, ganttCharts.Key, "true");
```

### Create Billing Cycles

```csharp
// Create monthly billing cycle
var monthly = await subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(
    PlanKey: proPlan.Key,
    Key: "pro-monthly",
    DisplayName: "Pro Monthly",
    DurationValue: 1,
    DurationUnit: "months"
));

// Create yearly billing cycle
var yearly = await subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(
    PlanKey: proPlan.Key,
    Key: "pro-yearly",
    DisplayName: "Pro Yearly",
    DurationValue: 1,
    DurationUnit: "years"
));
```

### Create Customers and Subscriptions

```csharp
// Create customer
var customer = await subscrio.Customers.CreateCustomerAsync(new CreateCustomerDto(
    Key: "customer-123",
    DisplayName: "John Doe"
));

// Create subscription
var subscription = await subscrio.Subscriptions.CreateSubscriptionAsync(new CreateSubscriptionDto(
    Key: "sub-001",
    CustomerKey: customer.Key,
    BillingCycleKey: monthly.Key
));

// Add feature override
await subscrio.Subscriptions.AddFeatureOverrideAsync(
    subscription.Key,
    maxProjects.Key,
    "100",
    OverrideType.Permanent
);
```

### Check Feature Values

```csharp
// Check if feature is enabled
var hasGanttCharts = await subscrio.FeatureChecker.IsEnabledForCustomerAsync(
    customer.Key,
    product.Key,
    ganttCharts.Key
);

// Get feature value
var maxProjectsValue = await subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
    customer.Key,
    product.Key,
    maxProjects.Key
);

// Get all features for a customer on a product
var allFeatures = await subscrio.FeatureChecker.GetAllFeaturesForCustomerAsync(
    customer.Key,
    product.Key
);
```

## Feature Resolution Hierarchy

Feature values use the same resolution hierarchy. See the [Subscrio hub README](https://github.com/subscrio/subscrio#feature-resolution-hierarchy) for details.

## Stripe Integration

Subscrio accepts supported Stripe events after your application verifies them. Stripe remains responsible for payment processing. See the [Subscrio hub README](https://github.com/subscrio/subscrio#stripe-integration) for an overview.

**Important**: Subscrio does NOT verify Stripe webhook signatures. You must verify signatures before passing events to Subscrio.

### Process Stripe Webhooks

```csharp
using Stripe;

// In your webhook endpoint (after verifying Stripe signature)
[HttpPost("webhooks/stripe")]
public async Task<IActionResult> HandleStripeWebhook()
{
    var json = await new StreamReader(Request.Body).ReadToEndAsync();
    var stripeSignature = Request.Headers["Stripe-Signature"].ToString();
    
    try
    {
        // Verify webhook signature
        var stripeEvent = EventUtility.ConstructEvent(
            json,
            stripeSignature,
            _configuration["Stripe:WebhookSecret"]
        );
        
        // Process verified event
        await _subscrio.Stripe.ProcessStripeEventAsync(stripeEvent);
        return Ok(new { received = true });
    }
    catch (StripeException ex)
    {
        return BadRequest(new { error = ex.Message });
    }
}
```

### Create Stripe Subscription

```csharp
var subscription = await subscrio.Stripe.CreateStripeSubscriptionAsync(
    customerKey: customer.Key,
    planKey: proPlan.Key,
    billingCycleKey: monthly.Key,
    stripePriceId: "price_123"
);
```

## .NET Support

Full .NET support with comprehensive type definitions and async/await patterns:

```csharp
using Subscrio.Core;
using Subscrio.Core.Application.DTOs;

// All APIs are fully typed
ProductDto product = await subscrio.Products.CreateProductAsync(new CreateProductDto(
    Key: "my-product",
    DisplayName: "My Product"
));

// DTOs are strongly typed
var feature = await subscrio.Features.CreateFeatureAsync(new CreateFeatureDto(
    Key: "max-projects",
    DisplayName: "Max Projects",
    ValueType: "numeric",
    DefaultValue: "10"
));
```

### Key Concepts

**Keys vs IDs**: All public APIs use **keys** (string identifiers like `"my-product"`) rather than internal IDs. Keys are:
- Human-readable and memorable
- Globally unique within their scope
- Immutable once created
- Used in all method calls and references

**DTOs**: All create/update operations use DTOs (Data Transfer Objects) with validation:
- `CreateProductDto`, `CreateFeatureDto`, `CreatePlanDto`, etc.
- All fields are validated before processing
- Type-safe with full C# type inference

**Async/Await**: All operations are asynchronous:
- All methods return `Task<T>` or `Task`
- Use `await` for all Subscrio operations
- Proper async/await patterns throughout

**Dependency Injection**: Recommended for web applications:
- Register with `AddSubscrio()` extension method
- Use `ServiceLifetime.Scoped` for web apps
- Automatic resource management

## Best Practices

1. **Use Dependency Injection** - Register Subscrio with `AddSubscrio()` in web applications
2. **Scoped Lifetime** - Use `ServiceLifetime.Scoped` for web apps to avoid tracking conflicts
3. **Schema Installation** - Run `InstallSchemaAsync()` once during application startup
4. **Error Handling** - Handle `ValidationException`, `NotFoundException`, `ConflictException` appropriately
5. **Feature Keys** - Use lowercase alphanumeric keys with hyphens (e.g., `max-projects`)
6. **Customer keys** - Use your own stable customer keys; set `ExternalBillingId` only when integrating with Stripe billing
7. **Database Connections** - Subscrio manages its own DbContext lifecycle based on service lifetime

## License

MIT License - see [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome! Please open issues and pull requests in this repository. For org-wide contribution guidelines, see the [Subscrio hub CONTRIBUTING guide](https://github.com/subscrio/subscrio/blob/main/CONTRIBUTING.md).

## Support

- 📖 [Subscrio hub](https://github.com/subscrio/subscrio)
- 🐛 [Report Issues](https://github.com/subscrio/subscrio-dotnet/issues)
- 💬 [Discussions](https://github.com/subscrio/subscrio/discussions) (org-wide)
- 📚 [Testing Guide](tests/README.md)
- 📋 [Core API reference](https://github.com/subscrio/docs/blob/main/docs/reference/core-overview.md)

<p align="center">
  Maintained by <a href="https://github.com/jasenf">Jasen Fici</a> · Part of the <a href="https://github.com/subscrio">Subscrio</a> org
</p>


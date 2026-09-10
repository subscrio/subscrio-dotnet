# Subscrio .NET Testing Guide

Complete guide for testing Subscrio.Core using end-to-end tests with real PostgreSQL databases.

## Philosophy

**We test the PUBLIC API with REAL databases. No mocks.**

- Test complete workflows end-to-end
- Each test suite gets a fresh PostgreSQL database
- Test actual behavior, not implementation details
- Focus on the public API that users will call

## Test Framework

**xUnit** - .NET testing framework

## Requirements

- **.NET 8.0 SDK** - Required to build and run tests
- **PostgreSQL 15+** - Installed and running locally
- **Database Access** - User must have CREATEDB privilege

## Quick Start

### 1. Install PostgreSQL

**Windows:**
- Download from [postgresql.org](https://www.postgresql.org/download/windows/)
- Install PostgreSQL 15 or later
- Ensure PostgreSQL service is running

**macOS (Homebrew):**
```bash
brew install postgresql@15
brew services start postgresql@15
```

**Ubuntu/Debian:**
```bash
sudo apt-get install postgresql-15
sudo systemctl start postgresql
```

### 2. Configure Database Connection

The tests support multiple configuration methods (in priority order):

1. **Environment Variables** (highest priority)
2. **appsettings.json** or **appsettings.Development.json**
3. **Default values** (fallback)

#### Option 1: Using appsettings.json (Recommended)

Copy `appsettings.example.json` to `appsettings.json` (appsettings.json is gitignored to protect credentials), then edit `tests/appsettings.json`:

```json
{
  "TestDatabase": {
    "ConnectionString": "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres",
    "KeepTestDb": false
  }
}
```

For development overrides, create `appsettings.Development.json`:

```json
{
  "TestDatabase": {
    "ConnectionString": "Host=localhost;Port=5432;Database=postgres;Username=myuser;Password=mypassword",
    "KeepTestDb": true
  }
}
```

**Note:** `appsettings.Development.json` overrides `appsettings.json` and is typically gitignored for local development.

#### Option 2: Using Environment Variables

**PowerShell:**
```powershell
$env:TEST_DATABASE_URL = "Host=localhost;Port=5432;Database=postgres;Username=myuser;Password=mypassword"
```

**Bash:**
```bash
export TEST_DATABASE_URL="Host=localhost;Port=5432;Database=postgres;Username=myuser;Password=mypassword"
```

**Default (if neither appsettings.json nor environment variable is set):**
```
Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres
```

### 3. Run Tests

**IMPORTANT**: The test project targets multiple frameworks (net8.0, net9.0, net10.0). When you run `dotnet test` without specifying a framework, the test SDK runs all frameworks in parallel, which can exhaust the PostgreSQL connection pool.

**Recommended: Run one framework at a time (built-in, no script needed)**
```powershell
cd .\tests
dotnet test -f net8.0
dotnet test -f net9.0
dotnet test -f net10.0
```

**Alternative: Use convenience script**
```powershell
cd .\tests
.\run-tests-sequential.ps1
```

**Not Recommended: Run all frameworks in parallel**
```powershell
cd .\tests
dotnet test  # May cause "too many clients already" errors
```

**Why?** The test SDK doesn't provide a built-in way to run multi-targeted tests sequentially from the project file. Running each framework separately with `-f` is the simplest and most reliable approach.

### 4. Run Tests with Output

```powershell
dotnet test tests/Subscrio.Core.Tests.csproj --logger "console;verbosity=detailed"
```

### 5. Run Specific Test

```powershell
dotnet test tests/Subscrio.Core.Tests.csproj --filter "FullyQualifiedName~ProductsTests"
```

### 6. Debug Mode (Keep Test Database)

To preserve the test database after tests complete for debugging:

**Option 1: Using appsettings.json**
```json
{
  "TestDatabase": {
    "KeepTestDb": true
  }
}
```

**Option 2: Using Environment Variable**

**PowerShell:**
```powershell
$env:KEEP_TEST_DB = "true"
dotnet test tests/Subscrio.Core.Tests.csproj
```

**Bash:**
```bash
export KEEP_TEST_DB=true
dotnet test tests/Subscrio.Core.Tests.csproj
```

This preserves the `subscrio_test` database for inspection after tests complete.

## Test Structure

```
tests/
├── Setup/
│   ├── TestDatabase.cs                    # Database creation/teardown utilities
│   ├── TestDatabaseAssemblyFixture.cs     # Assembly-level database setup/teardown
│   └── TestFixture.cs                     # Test data helper methods
└── E2E/
    ├── ProductsTests.cs
    ├── FeaturesTests.cs
    ├── PlansTests.cs
    ├── CustomersTests.cs
    ├── SubscriptionsTests.cs
    ├── BillingCyclesTests.cs
    ├── FeatureCheckerTests.cs             # CRITICAL - test resolution hierarchy
    ├── FeatureCheckerMultiSubscriptionTests.cs
    ├── ConfigSyncTests.cs
    ├── StripeIntegrationTests.cs
    └── PerformanceTests.cs
```

## Database Setup Pattern

**Current Implementation**: Uses assembly-level fixture with a shared test database for all tests.

Each test file follows this pattern:

```csharp
public class ProductsTests : IDisposable
{
    private readonly Subscrio _subscrio;
    private readonly TestFixtures _fixtures;

    public ProductsTests()
    {
        // Ensure database is initialized
        TestDatabaseAssemblyFixture.EnsureInitialized();
        
        // Create Subscrio instance with test database connection
        var connectionString = TestDatabaseAssemblyFixture.GetTestConnectionString();
        var config = new SubscrioConfig
        {
            Database = new DatabaseConfig
            {
                ConnectionString = connectionString,
                Ssl = false,
                PoolSize = 5, // Reduced pool size for tests to avoid connection exhaustion
                DatabaseType = DatabaseType.PostgreSQL
            }
        };
        
        _subscrio = new Subscrio(config);
        _fixtures = new TestFixtures(_subscrio);
    }

    public void Dispose()
    {
        _subscrio?.Dispose(); // Properly close database connections
    }

    [Fact]
    public async Task CreatesProductWithValidData()
    {
        // Use TestFixtures helper for unique keys
        var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
        {
            ["DisplayName"] = "Test Product"
        });

        product.Should().NotBeNull();
        product.Key.Should().NotBeNullOrEmpty();
        product.DisplayName.Should().Be("Test Product");
    }
}
```

### Database Setup Architecture

**Assembly Fixture** (`TestDatabaseAssemblyFixture.cs`):
- Creates a single shared test database per framework version (`subscrio_test_net80`, `subscrio_test_net90`, etc.)
- Runs once before all test files (static initialization)
- Handles cleanup of dangling test databases
- Provides connection string via `GetTestConnectionString()` static method
- Each test class creates its own `Subscrio` instance but connects to the same database

**Database Utilities** (`TestDatabase.cs`):
- `SetupTestDatabaseAsync()` - Creates shared test database
- `TeardownTestDatabaseAsync()` - Cleans up test database
- `CleanupDanglingTestDatabasesAsync()` - Removes orphaned test databases
- `GetTestConnectionString()` - Returns connection string for test database
- Supports `KEEP_TEST_DB=true` for debugging

## Public API Test Coverage

Every public method must have tests:

### Subscrio Main Class
- `new Subscrio(config)`
- `InstallSchemaAsync()`
- `VerifySchemaAsync()`
- `MigrateAsync()`
- `DropSchemaAsync()`
- `Dispose()`

### ProductManagementService (`subscrio.Products`)
- `CreateProductAsync(dto)` - Create new product
- `UpdateProductAsync(key, dto)` - Update existing product
- `GetProductAsync(key)` - Get product by key
- `ListProductsAsync(filters?)` - List products with optional filters
- `DeleteProductAsync(key)` - Delete product (must be archived)
- `ArchiveProductAsync(key)` - Archive product
- `UnarchiveProductAsync(key)` - Unarchive product
- `AssociateFeatureAsync(productKey, featureKey)` - Associate feature with product
- `DissociateFeatureAsync(productKey, featureKey)` - Remove feature from product

### FeatureManagementService (`subscrio.Features`)
- `CreateFeatureAsync(dto)` - Create new feature
- `UpdateFeatureAsync(key, dto)` - Update existing feature
- `GetFeatureAsync(key)` - Get feature by key
- `ListFeaturesAsync(filters?)` - List features with optional filters
- `DeleteFeatureAsync(key)` - Delete feature (must be archived)
- `ArchiveFeatureAsync(key)` - Archive feature
- `UnarchiveFeatureAsync(key)` - Unarchive feature
- `GetFeaturesByProductAsync(productKey)` - Get features for a product

### PlanManagementService (`subscrio.Plans`)
- `CreatePlanAsync(dto)` - Create new plan
- `UpdatePlanAsync(productKey, planKey, dto)` - Update existing plan
- `GetPlanAsync(productKey, planKey)` - Get plan by product and plan keys
- `ListPlansAsync(filters?)` - List plans with optional filters
- `GetPlansByProductAsync(productKey)` - Get plans for a product
- `DeletePlanAsync(productKey, planKey)` - Delete plan (must be archived)
- `ArchivePlanAsync(productKey, planKey)` - Archive plan
- `UnarchivePlanAsync(productKey, planKey)` - Unarchive plan
- `SetFeatureValueAsync(productKey, planKey, featureKey, value)` - Set feature value for plan
- `RemoveFeatureValueAsync(productKey, planKey, featureKey)` - Remove feature value from plan
- `GetFeatureValueAsync(productKey, planKey, featureKey)` - Get feature value for plan

### CustomerManagementService (`subscrio.Customers`)
- `CreateCustomerAsync(dto)` - Create new customer
- `UpdateCustomerAsync(key, dto)` - Update existing customer
- `GetCustomerAsync(key)` - Get customer by key
- `ListCustomersAsync(filters?)` - List customers with optional filters
- `ArchiveCustomerAsync(key)` - Archive customer
- `UnarchiveCustomerAsync(key)` - Unarchive customer
- `DeleteCustomerAsync(key)` - Delete customer

### SubscriptionManagementService (`subscrio.Subscriptions`)
- `CreateSubscriptionAsync(dto)` - Create new subscription
- `UpdateSubscriptionAsync(subscriptionKey, dto)` - Update existing subscription
- `GetSubscriptionAsync(subscriptionKey)` - Get subscription by key
- `GetSubscriptionByStripeIdAsync(stripeId)` - Get subscription by Stripe ID
- `ListSubscriptionsAsync(filters?)` - List subscriptions with optional filters
- `GetSubscriptionsByCustomerAsync(customerKey)` - Get all subscriptions for customer
- `GetActiveSubscriptionsByCustomerAsync(customerKey)` - Get active subscriptions for customer
- `CancelSubscriptionAsync(subscriptionKey)` - Cancel subscription
- `ExpireSubscriptionAsync(subscriptionKey)` - Expire subscription
- `RenewSubscriptionAsync(subscriptionKey)` - Renew subscription
- `DeleteSubscriptionAsync(subscriptionKey)` - Delete subscription
- `AddFeatureOverrideAsync(subscriptionKey, featureKey, value, type)` - Add feature override
- `RemoveFeatureOverrideAsync(subscriptionKey, featureKey)` - Remove feature override
- `ClearTemporaryOverridesAsync(subscriptionKey)` - Clear temporary overrides

### BillingCycleManagementService (`subscrio.BillingCycles`)
- `CreateBillingCycleAsync(dto)` - Create new billing cycle
- `UpdateBillingCycleAsync(productKey, planKey, key, dto)` - Update existing billing cycle
- `GetBillingCycleAsync(productKey, planKey, key)` - Get billing cycle
- `GetBillingCyclesByPlanAsync(productKey, planKey)` - Get billing cycles for plan
- `ListBillingCyclesAsync(filters?)` - List billing cycles with optional filters
- `DeleteBillingCycleAsync(productKey, planKey, key)` - Delete billing cycle

### FeatureCheckerService (`subscrio.FeatureChecker`) - **CRITICAL**
- `IsEnabledAsync(customerKey, featureKey)` - Check if feature is enabled
- `GetValueAsync(customerKey, featureKey, defaultValue?)` - Get feature value for customer
- `GetAllFeaturesAsync(customerKey)` - Get all feature values for customer
- `GetFeaturesForSubscriptionAsync(subscriptionKey)` - Get features for specific subscription
- `HasPlanAccessAsync(customerKey, planKey)` - Check if customer has plan access
- `GetActivePlansAsync(customerKey)` - Get active plans for customer
- `GetFeatureUsageSummaryAsync(customerKey)` - Get feature usage summary

### StripeIntegrationService (`subscrio.Stripe`)
- `ProcessStripeEventAsync(event)` - Process verified Stripe webhook event
- `CreateCheckoutSessionAsync(...)` - Create a Stripe Checkout session URL
- `ProcessStripeEventAsync(stripeEvent)` - Sync a verified Stripe webhook event

## Example Test: Feature Resolution Hierarchy

This is the **most critical test** - it verifies the core feature resolution logic:

```csharp
[Fact]
public async Task ResolvesFromSubscriptionOverridePlanValueFeatureDefault()
{
    // 1. Create product
    var product = await _subscrio.Products.CreateProductAsync(new CreateProductDto(
        Key: "test-product",
        DisplayName: "Test Product"
    ));

    // 2. Create feature with default value
    var feature = await _subscrio.Features.CreateFeatureAsync(new CreateFeatureDto(
        Key: "max-projects",
        DisplayName: "Max Projects",
        ValueType: "numeric",
        DefaultValue: "10"
    ));

    // 3. Associate feature with product
    await _subscrio.Products.AssociateFeatureAsync(product.Key, feature.Key);

    // 4. Create plan and set feature value
    var plan = await _subscrio.Plans.CreatePlanAsync(new CreatePlanDto(
        ProductKey: product.Key,
        Key: "pro",
        DisplayName: "Pro Plan"
    ));
    await _subscrio.Plans.SetFeatureValueAsync(plan.Key, feature.Key, "50");

    // 5. Create customer
    var customer = await _subscrio.Customers.CreateCustomerAsync(new CreateCustomerDto(
        Key: "test-customer"
    ));

    // 6. Create billing cycle
    var billingCycle = await _subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(
        PlanKey: plan.Key,
        Key: "monthly",
        DisplayName: "Monthly",
        DurationUnit: "month"
    ));

    // 7. Create subscription
    var subscription = await _subscrio.Subscriptions.CreateSubscriptionAsync(new CreateSubscriptionDto(
        CustomerKey: customer.Key,
        PlanKey: plan.Key,
        BillingCycleKey: billingCycle.Key
    ));

    // TEST: Should resolve from plan value
    var value = await _subscrio.FeatureChecker.GetValueAsync(customer.Key, feature.Key);
    value.Should().Be("50");

    // 8. Add subscription override
    await _subscrio.Subscriptions.AddFeatureOverrideAsync(
        subscription.Key,
        feature.Key,
        "100",
        "permanent"
    );

    // TEST: Should now resolve from subscription override
    value = await _subscrio.FeatureChecker.GetValueAsync(customer.Key, feature.Key);
    value.Should().Be("100");

    // 9. Remove override
    await _subscrio.Subscriptions.RemoveFeatureOverrideAsync(subscription.Key, feature.Key);

    // TEST: Should fall back to plan value
    value = await _subscrio.FeatureChecker.GetValueAsync(customer.Key, feature.Key);
    value.Should().Be("50");

    // 10. Remove plan feature value
    await _subscrio.Plans.RemoveFeatureValueAsync(plan.Key, feature.Key);

    // TEST: Should fall back to feature default
    value = await _subscrio.FeatureChecker.GetValueAsync(customer.Key, feature.Key);
    value.Should().Be("10");
}
```

## Test Fixtures and Unique Key Strategy

**CRITICAL**: Always use the `TestFixtures` helper to generate unique keys. This ensures tests are order-independent and can run in parallel.

### Using TestFixtures Helper

The `TestFixtures` class automatically generates unique keys with timestamps:

```csharp
var fixtures = new TestFixtures(_subscrio);

// Create a product with unique key (auto-generated)
var product = await fixtures.CreateProductAsync(new Dictionary<string, object>
{
    ["DisplayName"] = "Test Product"
});
// Key will be: "product-{timestamp}"

// Create a feature with unique key
var feature = await fixtures.CreateFeatureAsync(new Dictionary<string, object>
{
    ["DisplayName"] = "Max Projects",
    ["ValueType"] = "numeric",
    ["DefaultValue"] = "10"
});
// Key will be: "feature-{timestamp}"

// Create a complete product setup
var setup = await fixtures.SetupCompleteProductAsync();
// Returns: { Product, Features, Plans }
```

### Explicit Keys for Test Scenarios

When you need a specific key for testing (e.g., duplicate key tests), explicitly pass it via overrides:

```csharp
// For duplicate key test - explicitly set the key
await fixtures.CreateProductAsync(new Dictionary<string, object>
{
    ["Key"] = "duplicate-key",  // Explicit override for test scenario
    ["DisplayName"] = "Product 1"
});

await Assert.ThrowsAsync<ConflictException>(async () =>
{
    await fixtures.CreateProductAsync(new Dictionary<string, object>
    {
        ["Key"] = "duplicate-key",  // Same key to trigger conflict
        ["DisplayName"] = "Product 2"
    });
});
```

### Benefits of Unique Keys

- **Order-Independent**: Tests can run in any order without conflicts
- **Parallelization Ready**: Tests can run in parallel without key collisions
- **Robust**: No reliance on test execution order
- **Consistent**: All tests follow the same pattern

### ❌ Don't Use Fixed Keys

```csharp
// ❌ BAD - Fixed key, could conflict if test order changes
var product = await _subscrio.Products.CreateProductAsync(new CreateProductDto(
    Key: "test-product",
    DisplayName: "Test Product"
));

// ✅ GOOD - Unique key via helper
var product = await _fixtures.CreateProductAsync(new Dictionary<string, object>
{
    ["DisplayName"] = "Test Product"
});
```

## Configuration

### Configuration Priority

1. **Environment Variables** (highest priority)
2. **appsettings.Development.json** (if exists)
3. **appsettings.json**
4. **Default values** (fallback)

### Configuration Options

**Using appsettings.json:**
```json
{
  "TestDatabase": {
    "ConnectionString": "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres",
    "KeepTestDb": false
  }
}
```

**Using Environment Variables:**
- `TEST_DATABASE_URL` - Override test database connection string
- `KEEP_TEST_DB` - Set to `true` to preserve test database after tests

**Note:** Environment variables always override appsettings.json values.

## Troubleshooting

### "Database does not exist"
```powershell
# Connect to PostgreSQL and create the postgres database if needed
psql -U postgres -c "CREATE DATABASE postgres;"
```

### "Connection refused"
- Ensure PostgreSQL is running
- Check PostgreSQL is listening on port 5432
- Verify credentials match your local setup

### "Permission denied to create database"
- Ensure your PostgreSQL user has CREATEDB privilege
- Run: `ALTER USER postgres CREATEDB;` in psql

### Tests hang or timeout
- Check for unclosed database connections
- Assembly fixture handles database creation/cleanup automatically
- Tests run sequentially (xUnit default)
- Use `KEEP_TEST_DB=true` to preserve test database for investigation

### "Too many clients already" / "Too many connections"
- **Root Cause**: Running tests for multiple frameworks in parallel can exhaust PostgreSQL connection pool
- **Solution**: Run tests sequentially using `.\run-tests-sequential.ps1` or run one framework at a time with `dotnet test -f net8.0`
- **Connection Management**: All test classes implement `IDisposable` to properly close database connections
- **Pool Size**: Test classes use reduced pool size (5) to minimize connection usage
- **Check active connections**: `SELECT count(*) FROM pg_stat_activity;`
- **Assembly fixture**: Includes cleanup of dangling test databases
- **Terminate zombie connections**: From failed test runs using `SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname LIKE 'subscrio_test%';`

### Test Database Issues
- **Keep test database for debugging**: `$env:KEEP_TEST_DB = "true"; dotnet test`
- **Manual cleanup**: Connect to postgres and run `DROP DATABASE IF EXISTS subscrio_test_netXX;` (where XX is framework version)
- **Check for orphaned databases**: Look for databases matching `subscrio_test_net*` pattern
- **Assembly fixture logs**: Check console output for database setup/teardown messages (one message per framework version)

## Best Practices

1. **Test Behavior, Not Implementation**
   - Call public API methods
   - Assert on outcomes
   - Don't test internal repository or domain service methods directly

2. **Use Real Data**
   - Don't mock the database
   - Use `TestFixtures` helper to create test data with unique keys
   - Each test starts with a clean database (created once per test run)
   - Always use unique keys via `TestFixtures` helper (except for duplicate key tests)

3. **Descriptive Test Names**
   ```csharp
   [Fact]
   public async Task ThrowsConflictExceptionWhenCreatingProductWithDuplicateKey()
   {
       // ...
   }
   ```

4. **Test Error Cases**
   ```csharp
   await Assert.ThrowsAsync<ConflictException>(async () =>
   {
       await _subscrio.Products.CreateProductAsync(new CreateProductDto(
           Key: "existing-key",
           DisplayName: "Product"
       ));
   });
   ```

5. **Keep Tests Fast**
   - Use unique keys to enable future parallelization
   - Tests currently run sequentially (xUnit default)
   - Use database transactions for faster cleanup (if needed)
   - Don't sleep/wait unnecessarily

6. **Cover All Public Methods**
   - Every method in `subscrio.*` namespace
   - Success paths
   - Error paths
   - Edge cases

## Running Tests from Command Line

**Run all tests sequentially (recommended):**
```powershell
cd .\tests
.\run-tests-sequential.ps1
```

**Run all tests (may hit connection limits with multiple frameworks):**
```powershell
dotnet test tests/Subscrio.Core.Tests.csproj
```

**Run tests for specific framework:**
```powershell
dotnet test tests/Subscrio.Core.Tests.csproj -f net8.0
```

**Run with detailed output:**
```powershell
dotnet test tests/Subscrio.Core.Tests.csproj --logger "console;verbosity=detailed"
```

**Run specific test class:**
```powershell
dotnet test tests/Subscrio.Core.Tests.csproj --filter "FullyQualifiedName~ProductsTests"
```

**Run specific test method:**
```powershell
dotnet test tests/Subscrio.Core.Tests.csproj --filter "FullyQualifiedName~ProductsTests.CreatesProductWithValidData"
```

**Run tests and generate coverage:**
```powershell
dotnet test tests/Subscrio.Core.Tests.csproj --collect:"XPlat Code Coverage"
```

## CI/CD Integration

**GitHub Actions Example:**

```yaml
name: Test
on: [push, pull_request]

jobs:
  test:
    runs-on: ubuntu-latest
    
    services:
      postgres:
        image: postgres:15
        env:
          POSTGRES_PASSWORD: postgres
        options: >-
          --health-cmd pg_isready
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5
        ports:
          - 5432:5432

    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0.x'
      - run: dotnet restore
      - run: dotnet test --no-restore
        env:
          TEST_DATABASE_URL: Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres
```

## Support

- See [README.md](../README.md) for project setup
- See [Subscrio documentation](https://github.com/subscrio/docs) for specifications
- Run `dotnet test --logger "console;verbosity=detailed"` for interactive testing during development


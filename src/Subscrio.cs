using Npgsql;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Hooks;
using Subscrio.Core.Application.Repositories;
using Subscrio.Core.Application.Services;
using Subscrio.Core.Application.Validators;
using Subscrio.Core.Config;
using Subscrio.Core.Infrastructure.Database;
using Subscrio.Core.Infrastructure.Repositories;

namespace Subscrio.Core;

/// <summary>
/// Entry point for the Subscrio library: schema lifecycle and domain services.
/// </summary>
public class Subscrio : IDisposable
{
    private readonly SubscrioDbContext _db;
    private readonly SchemaInstaller _installer;
    private readonly Npgsql.NpgsqlDataSource? _dataSource;
    private readonly string? _adminPassphrase;

    // Repositories (private)
    private readonly IProductRepository _productRepo;
    private readonly IFeatureRepository _featureRepo;
    private readonly IPlanRepository _planRepo;
    private readonly ICustomerRepository _customerRepo;
    private readonly ISubscriptionRepository _subscriptionRepo;
    private readonly IBillingCycleRepository _billingCycleRepo;

    /// <summary>Product create/update/list and feature association.</summary>
    public ProductManagementService Products { get; }
    /// <summary>Feature catalog management.</summary>
    public FeatureManagementService Features { get; }
    /// <summary>Plan management and plan feature values.</summary>
    public PlanManagementService Plans { get; }
    /// <summary>Customer create/update/list and lifecycle.</summary>
    public CustomerManagementService Customers { get; }
    /// <summary>Subscription create/update and period transitions.</summary>
    public SubscriptionManagementService Subscriptions { get; }
    /// <summary>Billing cycle management for plans.</summary>
    public BillingCycleManagementService BillingCycles { get; }
    /// <summary>Entitlement checks against active subscriptions.</summary>
    public FeatureCheckerService FeatureChecker { get; }
    /// <summary>Optional Stripe event processing and sync helpers.</summary>
    public StripeIntegrationService Stripe { get; }
    /// <summary>Declarative config sync for products, features, plans, and billing cycles.</summary>
    public ConfigSyncService ConfigSync { get; }
    /// <summary>Before-mutation hook dispatcher registered from config.</summary>
    public HookDispatcher Hooks { get; }

    /// <summary>
    /// Creates a Subscrio instance from configuration (database, optional Stripe, hooks, initial config).
    /// </summary>
    /// <param name="config">Library configuration.</param>
    public Subscrio(SubscrioConfig config)
    {
        var dbResult = DatabaseInitializer.InitializeDatabase(config.Database);
        _db = dbResult.DbContext;
        _dataSource = dbResult.DataSource;
        _installer = new SchemaInstaller(_db);

        _productRepo = new EfProductRepository(_db);
        _featureRepo = new EfFeatureRepository(_db);
        _planRepo = new EfPlanRepository(_db);
        _customerRepo = new EfCustomerRepository(_db);
        _subscriptionRepo = new EfSubscriptionRepository(_db);
        _billingCycleRepo = new EfBillingCycleRepository(_db);

        var hooks = new HookDispatcher(config.Hooks);
        Hooks = hooks;

        Products = new ProductManagementService(
            _productRepo,
            _featureRepo,
            new CreateProductDtoValidator(),
            new UpdateProductDtoValidator(),
            new ProductFilterDtoValidator()
        );
        Features = new FeatureManagementService(
            _featureRepo,
            _productRepo,
            new CreateFeatureDtoValidator(),
            new UpdateFeatureDtoValidator(),
            new FeatureFilterDtoValidator()
        );
        Plans = new PlanManagementService(
            _planRepo,
            _productRepo,
            _featureRepo,
            _billingCycleRepo,
            _subscriptionRepo,
            new CreatePlanDtoValidator(),
            new UpdatePlanDtoValidator(),
            new PlanFilterDtoValidator()
        );
        Customers = new CustomerManagementService(
            _customerRepo,
            new CreateCustomerDtoValidator(),
            new UpdateCustomerDtoValidator(),
            new CustomerFilterDtoValidator(),
            hooks
        );
        Subscriptions = new SubscriptionManagementService(
            _subscriptionRepo,
            _customerRepo,
            _planRepo,
            _billingCycleRepo,
            _featureRepo,
            _productRepo,
            new CreateSubscriptionDtoValidator(),
            new UpdateSubscriptionDtoValidator(),
            new SubscriptionFilterDtoValidator(),
            new DetailedSubscriptionFilterDtoValidator(),
            hooks
        );
        BillingCycles = new BillingCycleManagementService(
            _billingCycleRepo,
            _planRepo,
            _productRepo,
            _subscriptionRepo,
            new CreateBillingCycleDtoValidator(),
            new UpdateBillingCycleDtoValidator(),
            new BillingCycleFilterDtoValidator()
        );
        FeatureChecker = new FeatureCheckerService(
            _subscriptionRepo,
            _planRepo,
            _featureRepo,
            _customerRepo,
            _productRepo
        );
        Stripe = new StripeIntegrationService(
            _subscriptionRepo,
            _customerRepo,
            _planRepo,
            _billingCycleRepo,
            config.Stripe?.SecretKey,
            hooks,
            _productRepo
        );
        ConfigSync = new ConfigSyncService(
            Products,
            Features,
            Plans,
            BillingCycles
        );
        _initialConfig = config.InitialConfig;
        _adminPassphrase = config.AdminPassphrase;
    }

    private readonly InitialConfigOptions? _initialConfig;

    /// <summary>
    /// Run initial config sync if InitialConfig was provided to the constructor.
    /// Call this after construction (e.g. after InstallSchemaAsync/VerifySchemaAsync) to apply the config.
    /// </summary>
    /// <returns>The sync report, or null if no InitialConfig was set</returns>
    public async Task<ConfigSyncReport?> RunInitialConfigSyncAsync()
    {
        if (_initialConfig == null) return null;
        if (!string.IsNullOrWhiteSpace(_initialConfig.FilePath))
            return await ConfigSync.SyncFromFileAsync(_initialConfig.FilePath);
        if (_initialConfig.Config != null)
            return await ConfigSync.SyncFromJsonAsync(_initialConfig.Config);
        return null;
    }

    /// <summary>
    /// Install database schema
    /// </summary>
    /// <param name="adminPassphrase">Optional passphrase. Defaults to <see cref="SubscrioConfig.AdminPassphrase"/> when omitted.</param>
    public async Task InstallSchemaAsync(string? adminPassphrase = null)
    {
        await _installer.InstallAsync(adminPassphrase ?? _adminPassphrase);
    }

    /// <summary>
    /// Verify schema installation
    /// Returns the schema version if installed, null otherwise
    /// </summary>
    public async Task<string?> VerifySchemaAsync()
    {
        return await _installer.VerifyAsync();
    }

    /// <summary>
    /// Run pending database migrations
    /// 
    /// Migrations are tracked via schema_version in system_config.
    /// This method runs only pending migrations and updates the version.
    /// 
    /// </summary>
    /// <returns>Number of migrations applied</returns>
    public async Task<int> MigrateAsync()
    {
        return await _installer.MigrateAsync();
    }

    /// <summary>
    /// Drop all database tables (WARNING: Destructive!).
    /// When an admin passphrase hash exists, the passphrase must match.
    /// </summary>
    /// <param name="adminPassphrase">Optional passphrase. Defaults to <see cref="SubscrioConfig.AdminPassphrase"/> when omitted.</param>
    public async Task DropSchemaAsync(string? adminPassphrase = null)
    {
        await _installer.DropSchemaAsync(adminPassphrase ?? _adminPassphrase);
    }

    /// <summary>
    /// Close database connections
    /// </summary>
    public void Dispose()
    {
        _db?.Dispose();
        _dataSource?.Dispose();
        GC.SuppressFinalize(this);
    }
}


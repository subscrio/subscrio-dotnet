using System.Text.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Subscrio.Core;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Infrastructure.Database;
using Subscrio.Sample;
using SubscrioInstance = Subscrio.Core.Subscrio;

// Global interactive mode flag
bool isInteractiveMode = false;

// JSON serializer options with camelCase naming to match TypeScript output
JsonSerializerOptions JsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

// ═══════════════════════════════════════════════════════════
// MAIN FUNCTION
// ═══════════════════════════════════════════════════════════

async Task Main()
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;

    var config = SampleConfig.LoadConfig();
    PrintHeader(config.Database.ConnectionString);

    // Check for command line arguments
    var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
    var isAutomated = args.Contains("--automated") || args.Contains("-a");
    var shouldRecreate = args.Contains("--recreate") || args.Contains("-r");

    // Prompt for demo start with options (unless automated)
    var choice = await PromptDemoStartAsync(isAutomated);

    if (choice == "q")
    {
        Console.WriteLine("Demo cancelled by user.");
        Environment.Exit(0);
    }

    // Set interactive mode based on user choice (but never in automated mode)
    isInteractiveMode = !isAutomated && choice == "i";

    var subscrio = new SubscrioInstance(config);

    try
    {
        // Drop and recreate schema if requested
        if (shouldRecreate)
        {
            Console.WriteLine("\n🔄 Dropping and recreating Subscrio tables...");
            await subscrio.DropSchemaAsync();
            Console.WriteLine("✅ Tables dropped successfully");
        }

        // Clean up existing demo entities
        await CleanupDemoEntitiesAsync(subscrio);

        await RunPhase1_SystemSetupAsync(subscrio);
        await RunPhase2_TrialStartAsync(subscrio);
        await RunPhase3_TrialToPurchaseAsync(subscrio);
        await RunPhase4_PlanUpgradeAsync(subscrio);
        await RunPhase5_FeatureOverridesAsync(subscrio);
        await RunPhase6_SubscriptionRenewalAsync(subscrio);
        await RunPhase7_DowngradeToFreeAsync(subscrio);
        await RunPhase8_SummaryAsync();
    }
    catch (Exception error)
    {
        Console.Error.WriteLine("\n❌ Error during demo execution:");
        Console.Error.WriteLine(error);
        Environment.Exit(1);
    }
    finally
    {
        subscrio.Dispose();
        Console.WriteLine("Database connections closed.\n");
    }
}

await Main();

// ═══════════════════════════════════════════════════════════
// PHASE METHODS (in order of execution)
// ═══════════════════════════════════════════════════════════

async Task RunPhase1_SystemSetupAsync(SubscrioInstance subscrio)
{
    PrintPhase(1, "System Setup");

    // Step 1: Install schema
    PrintStep(1, "Install Database Schema");
    await subscrio.InstallSchemaAsync();
    PrintSuccess("Database schema installed successfully");
    PrintDivider();

    if (isInteractiveMode)
    {
        await PromptForDatabaseInspectionAsync("Phase 1: System Setup", "Step 1: Schema Installation");
    }

    await SleepAsync(500);

    // Step 2: Create product
    PrintStep(2, "Create Product");
    PrintInfo("Creating the main product for our SaaS platform", 1);

    Console.WriteLine("📥 Input: subscrio.Products.CreateProductAsync(new CreateProductDto(");
    Console.WriteLine("  Key: \"projecthub\",");
    Console.WriteLine("  DisplayName: \"ProjectHub\",");
    Console.WriteLine("  Description: \"A modern project management platform\"");
    Console.WriteLine("))");

    var product = await subscrio.Products.CreateProductAsync(new CreateProductDto(
        Key: "projecthub",
        DisplayName: "ProjectHub",
        Description: "A modern project management platform"
    ));
    PrintSuccess($"Product created: {product.DisplayName} ({product.Key})");

    // Verify creation by fetching the product
    Console.WriteLine("📥 Input: subscrio.Products.GetProductAsync(\"projecthub\")");
    var fetchedProduct = await subscrio.Products.GetProductAsync(product.Key);
    Console.WriteLine($"📄 Output: Product DTO: {JsonSerializer.Serialize(fetchedProduct, JsonOptions)}");
    PrintDivider();

    if (isInteractiveMode)
    {
        await PromptForDatabaseInspectionAsync("Phase 1: System Setup", "Step 2: Product Creation Complete");
    }

    await SleepAsync(500);

    // Step 3: Create features
    PrintStep(3, "Create Features");

    var features = new[]
    {
        new CreateFeatureDto(
            Key: "max-projects",
            DisplayName: "Max Projects",
            ValueType: "numeric",
            DefaultValue: "3",
            Description: "Maximum number of projects"
        ),
        new CreateFeatureDto(
            Key: "max-users-per-project",
            DisplayName: "Max Users Per Project",
            ValueType: "numeric",
            DefaultValue: "5",
            Description: "Maximum users per project"
        ),
        new CreateFeatureDto(
            Key: "gantt-charts",
            DisplayName: "Gantt Charts",
            ValueType: "toggle",
            DefaultValue: "false",
            Description: "Advanced Gantt chart visualization"
        ),
        new CreateFeatureDto(
            Key: "custom-branding",
            DisplayName: "Custom Branding",
            ValueType: "toggle",
            DefaultValue: "false",
            Description: "White-label branding options"
        ),
        new CreateFeatureDto(
            Key: "api-access",
            DisplayName: "API Access",
            ValueType: "toggle",
            DefaultValue: "false",
            Description: "REST API access"
        )
    };

    foreach (var featureData in features)
    {
        // Serialize only the properties that TypeScript shows in input
        var inputDto = new
        {
            featureData.Key,
            featureData.DisplayName,
            featureData.ValueType,
            featureData.DefaultValue,
            featureData.Description
        };
        Console.WriteLine($"📥 Input: subscrio.Features.CreateFeatureAsync({JsonSerializer.Serialize(inputDto, JsonOptions)})");
        var feature = await subscrio.Features.CreateFeatureAsync(featureData);

        Console.WriteLine($"📥 Input: subscrio.Products.AssociateFeatureAsync(\"projecthub\", \"{feature.Key}\")");
        await subscrio.Products.AssociateFeatureAsync("projecthub", feature.Key);
        PrintSuccess($"Created feature: {feature.DisplayName} ({feature.Key})");

        // Verify creation by fetching the feature
        Console.WriteLine($"📥 Input: subscrio.Features.GetFeatureAsync(\"{feature.Key}\")");
        var fetchedFeature = await subscrio.Features.GetFeatureAsync(feature.Key);
        Console.WriteLine($"📄 Output: Feature DTO ({feature.Key}): {JsonSerializer.Serialize(fetchedFeature, JsonOptions)}");
    }
    PrintDivider();

    if (isInteractiveMode)
    {
        await PromptForDatabaseInspectionAsync("Phase 1: System Setup", "Step 3: Features Creation Complete");
    }

    await SleepAsync(500);

    // Step 4: Create plans
    PrintStep(4, "Create Plans with Billing Cycles");
    PrintInfo("Create each plan with its billing cycles and optional transition configuration", 1);

    // Free Plan (with forever billing cycle for transitions)
    Console.WriteLine("📥 Input: subscrio.Plans.CreatePlanAsync(new CreatePlanDto(");
    Console.WriteLine("  ProductKey: \"projecthub\",");
    Console.WriteLine("  Key: \"free\",");
    Console.WriteLine("  DisplayName: \"Free Plan\",");
    Console.WriteLine("  Description: \"Perfect for individuals and small teams\"");
    Console.WriteLine("))");

    var freePlan = await subscrio.Plans.CreatePlanAsync(new CreatePlanDto(
        ProductKey: "projecthub",
        Key: "free",
        DisplayName: "Free Plan",
        Description: "Perfect for individuals and small teams"
    ));
    PrintSuccess($"Created plan: {freePlan.DisplayName} ({freePlan.Key})");

    // Create only the forever billing cycle for free plan (used for downgrades later)
    Console.WriteLine("📥 Input: subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(");
    Console.WriteLine("  PlanKey: \"free\",");
    Console.WriteLine("  Key: \"free-forever\",");
    Console.WriteLine("  DisplayName: \"Forever\",");
    Console.WriteLine("  Description: \"Never-ending free access\",");
    Console.WriteLine("  DurationUnit: \"forever\"");
    Console.WriteLine("))");

    await subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(
        PlanKey: "free",
        Key: "free-forever",
        DisplayName: "Forever",
        Description: "Never-ending free access",
        DurationUnit: "forever"
    ));

    Console.WriteLine("📥 Input: subscrio.Plans.GetPlanAsync(\"free\")");
    Console.WriteLine("📥 Input: subscrio.BillingCycles.GetBillingCycleAsync(\"free-forever\")");
    Console.WriteLine($"📄 Output: Free Plan DTO: {JsonSerializer.Serialize(await subscrio.Plans.GetPlanAsync("free"), JsonOptions)}");
    Console.WriteLine($"📄 Output: Free Forever Billing Cycle DTO: {JsonSerializer.Serialize(await subscrio.BillingCycles.GetBillingCycleAsync("free-forever"), JsonOptions)}");

    // Starter Plan (with transition to free-forever)
    Console.WriteLine("📥 Input: subscrio.Plans.CreatePlanAsync(new CreatePlanDto(");
    Console.WriteLine("  ProductKey: \"projecthub\",");
    Console.WriteLine("  Key: \"starter\",");
    Console.WriteLine("  DisplayName: \"Starter Plan\",");
    Console.WriteLine("  Description: \"For growing teams\",");
    Console.WriteLine("  OnExpireTransitionToBillingCycleKey: \"free-forever\"");
    Console.WriteLine("))");

    var starterPlan = await subscrio.Plans.CreatePlanAsync(new CreatePlanDto(
        ProductKey: "projecthub",
        Key: "starter",
        DisplayName: "Starter Plan",
        Description: "For growing teams",
        OnExpireTransitionToBillingCycleKey: "free-forever"
    ));
    PrintSuccess($"Created plan: {starterPlan.DisplayName} ({starterPlan.Key}) with auto-transition to free plan");

    // Create billing cycles for starter plan
    Console.WriteLine("📥 Input: subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(");
    Console.WriteLine("  PlanKey: \"starter\",");
    Console.WriteLine("  Key: \"starter-monthly\",");
    Console.WriteLine("  DisplayName: \"Monthly\",");
    Console.WriteLine("  DurationValue: 1,");
    Console.WriteLine("  DurationUnit: \"months\"");
    Console.WriteLine("))");

    await subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(
        PlanKey: "starter",
        Key: "starter-monthly",
        DisplayName: "Monthly",
        DurationValue: 1,
        DurationUnit: "months"
    ));

    Console.WriteLine("📥 Input: subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(");
    Console.WriteLine("  PlanKey: \"starter\",");
    Console.WriteLine("  Key: \"starter-annual\",");
    Console.WriteLine("  DisplayName: \"Annual\",");
    Console.WriteLine("  DurationValue: 1,");
    Console.WriteLine("  DurationUnit: \"years\"");
    Console.WriteLine("))");

    await subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(
        PlanKey: "starter",
        Key: "starter-annual",
        DisplayName: "Annual",
        DurationValue: 1,
        DurationUnit: "years"
    ));

    Console.WriteLine("📥 Input: subscrio.Plans.GetPlanAsync(\"starter\")");
    Console.WriteLine("📥 Input: subscrio.BillingCycles.GetBillingCycleAsync(\"starter-monthly\")");
    Console.WriteLine("📥 Input: subscrio.BillingCycles.GetBillingCycleAsync(\"starter-annual\")");
    Console.WriteLine($"📄 Output: Starter Plan DTO: {JsonSerializer.Serialize(await subscrio.Plans.GetPlanAsync("starter"), JsonOptions)}");
    Console.WriteLine($"📄 Output: Starter Monthly Billing Cycle DTO: {JsonSerializer.Serialize(await subscrio.BillingCycles.GetBillingCycleAsync("starter-monthly"), JsonOptions)}");
    Console.WriteLine($"📄 Output: Starter Annual Billing Cycle DTO: {JsonSerializer.Serialize(await subscrio.BillingCycles.GetBillingCycleAsync("starter-annual"), JsonOptions)}");

    // Professional Plan (with transition to free-forever)
    Console.WriteLine("📥 Input: subscrio.Plans.CreatePlanAsync(new CreatePlanDto(");
    Console.WriteLine("  ProductKey: \"projecthub\",");
    Console.WriteLine("  Key: \"professional\",");
    Console.WriteLine("  DisplayName: \"Professional Plan\",");
    Console.WriteLine("  Description: \"For established businesses\",");
    Console.WriteLine("  OnExpireTransitionToBillingCycleKey: \"free-forever\"");
    Console.WriteLine("))");

    var professionalPlan = await subscrio.Plans.CreatePlanAsync(new CreatePlanDto(
        ProductKey: "projecthub",
        Key: "professional",
        DisplayName: "Professional Plan",
        Description: "For established businesses",
        OnExpireTransitionToBillingCycleKey: "free-forever"
    ));
    PrintSuccess($"Created plan: {professionalPlan.DisplayName} ({professionalPlan.Key}) with auto-transition to free plan");

    // Create billing cycles for professional plan
    Console.WriteLine("📥 Input: subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(");
    Console.WriteLine("  PlanKey: \"professional\",");
    Console.WriteLine("  Key: \"professional-monthly\",");
    Console.WriteLine("  DisplayName: \"Monthly\",");
    Console.WriteLine("  DurationValue: 1,");
    Console.WriteLine("  DurationUnit: \"months\"");
    Console.WriteLine("))");

    await subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(
        PlanKey: "professional",
        Key: "professional-monthly",
        DisplayName: "Monthly",
        DurationValue: 1,
        DurationUnit: "months"
    ));

    Console.WriteLine("📥 Input: subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(");
    Console.WriteLine("  PlanKey: \"professional\",");
    Console.WriteLine("  Key: \"professional-annual\",");
    Console.WriteLine("  DisplayName: \"Annual\",");
    Console.WriteLine("  DurationValue: 1,");
    Console.WriteLine("  DurationUnit: \"years\"");
    Console.WriteLine("))");

    await subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(
        PlanKey: "professional",
        Key: "professional-annual",
        DisplayName: "Annual",
        DurationValue: 1,
        DurationUnit: "years"
    ));

    Console.WriteLine("📥 Input: subscrio.Plans.GetPlanAsync(\"professional\")");
    Console.WriteLine("📥 Input: subscrio.BillingCycles.GetBillingCycleAsync(\"professional-monthly\")");
    Console.WriteLine("📥 Input: subscrio.BillingCycles.GetBillingCycleAsync(\"professional-annual\")");
    Console.WriteLine($"📄 Output: Professional Plan DTO: {JsonSerializer.Serialize(await subscrio.Plans.GetPlanAsync("professional"), JsonOptions)}");
    Console.WriteLine($"📄 Output: Professional Monthly Billing Cycle DTO: {JsonSerializer.Serialize(await subscrio.BillingCycles.GetBillingCycleAsync("professional-monthly"), JsonOptions)}");
    Console.WriteLine($"📄 Output: Professional Annual Billing Cycle DTO: {JsonSerializer.Serialize(await subscrio.BillingCycles.GetBillingCycleAsync("professional-annual"), JsonOptions)}");

    // Enterprise Plan (with transition to free-forever)
    Console.WriteLine("📥 Input: subscrio.Plans.CreatePlanAsync(new CreatePlanDto(");
    Console.WriteLine("  ProductKey: \"projecthub\",");
    Console.WriteLine("  Key: \"enterprise\",");
    Console.WriteLine("  DisplayName: \"Enterprise Plan\",");
    Console.WriteLine("  Description: \"For large organizations\",");
    Console.WriteLine("  OnExpireTransitionToBillingCycleKey: \"free-forever\"");
    Console.WriteLine("))");

    var enterprisePlan = await subscrio.Plans.CreatePlanAsync(new CreatePlanDto(
        ProductKey: "projecthub",
        Key: "enterprise",
        DisplayName: "Enterprise Plan",
        Description: "For large organizations",
        OnExpireTransitionToBillingCycleKey: "free-forever"
    ));
    PrintSuccess($"Created plan: {enterprisePlan.DisplayName} ({enterprisePlan.Key}) with auto-transition to free plan");

    // Create billing cycles for enterprise plan
    Console.WriteLine("📥 Input: subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(");
    Console.WriteLine("  PlanKey: \"enterprise\",");
    Console.WriteLine("  Key: \"enterprise-monthly\",");
    Console.WriteLine("  DisplayName: \"Monthly\",");
    Console.WriteLine("  DurationValue: 1,");
    Console.WriteLine("  DurationUnit: \"months\"");
    Console.WriteLine("))");

    await subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(
        PlanKey: "enterprise",
        Key: "enterprise-monthly",
        DisplayName: "Monthly",
        DurationValue: 1,
        DurationUnit: "months"
    ));

    Console.WriteLine("📥 Input: subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(");
    Console.WriteLine("  PlanKey: \"enterprise\",");
    Console.WriteLine("  Key: \"enterprise-annual\",");
    Console.WriteLine("  DisplayName: \"Annual\",");
    Console.WriteLine("  DurationValue: 1,");
    Console.WriteLine("  DurationUnit: \"years\"");
    Console.WriteLine("))");

    await subscrio.BillingCycles.CreateBillingCycleAsync(new CreateBillingCycleDto(
        PlanKey: "enterprise",
        Key: "enterprise-annual",
        DisplayName: "Annual",
        DurationValue: 1,
        DurationUnit: "years"
    ));

    Console.WriteLine("📥 Input: subscrio.Plans.GetPlanAsync(\"enterprise\")");
    Console.WriteLine("📥 Input: subscrio.BillingCycles.GetBillingCycleAsync(\"enterprise-monthly\")");
    Console.WriteLine("📥 Input: subscrio.BillingCycles.GetBillingCycleAsync(\"enterprise-annual\")");
    Console.WriteLine($"📄 Output: Enterprise Plan DTO: {JsonSerializer.Serialize(await subscrio.Plans.GetPlanAsync("enterprise"), JsonOptions)}");
    Console.WriteLine($"📄 Output: Enterprise Monthly Billing Cycle DTO: {JsonSerializer.Serialize(await subscrio.BillingCycles.GetBillingCycleAsync("enterprise-monthly"), JsonOptions)}");
    Console.WriteLine($"📄 Output: Enterprise Annual Billing Cycle DTO: {JsonSerializer.Serialize(await subscrio.BillingCycles.GetBillingCycleAsync("enterprise-annual"), JsonOptions)}");

    PrintDivider();

    if (isInteractiveMode)
    {
        await PromptForDatabaseInspectionAsync("Phase 1: System Setup", "Step 4: Plans Creation Complete");
    }

    // Set feature values for all plans
    PrintInfo("Configure feature limits and capabilities for each plan", 1);

    // Free plan: basic limits
    await subscrio.Plans.SetFeatureValueAsync("free", "max-projects", "1");
    await subscrio.Plans.SetFeatureValueAsync("free", "max-users-per-project", "3");
    PrintSuccess("Free plan: 1 project, 3 users per project");

    // Starter plan: moderate limits
    await subscrio.Plans.SetFeatureValueAsync("starter", "max-projects", "5");
    await subscrio.Plans.SetFeatureValueAsync("starter", "max-users-per-project", "10");
    PrintSuccess("Starter plan: 5 projects, 10 users per project");

    // Professional plan: higher limits + gantt charts
    await subscrio.Plans.SetFeatureValueAsync("professional", "max-projects", "25");
    await subscrio.Plans.SetFeatureValueAsync("professional", "max-users-per-project", "50");
    await subscrio.Plans.SetFeatureValueAsync("professional", "gantt-charts", "true");
    PrintSuccess("Professional plan: 25 projects, 50 users per project, Gantt charts enabled");

    // Enterprise plan: unlimited + all features
    await subscrio.Plans.SetFeatureValueAsync("enterprise", "max-projects", "999999");
    await subscrio.Plans.SetFeatureValueAsync("enterprise", "max-users-per-project", "999999");
    await subscrio.Plans.SetFeatureValueAsync("enterprise", "gantt-charts", "true");
    await subscrio.Plans.SetFeatureValueAsync("enterprise", "custom-branding", "true");
    await subscrio.Plans.SetFeatureValueAsync("enterprise", "api-access", "true");
    PrintSuccess("Enterprise plan: 999,999 projects/users, all features enabled");

    PrintDivider();

    if (isInteractiveMode)
    {
        await PromptForDatabaseInspectionAsync("Phase 1: System Setup", "Step 4: Plans, Billing Cycles, and Transitions Complete");
    }
}

async Task RunPhase2_TrialStartAsync(SubscrioInstance subscrio)
{
    PrintPhase(2, "Trial Start");

    // Step 1: Create customer
    PrintStep(1, "Create Customer");
    PrintInfo("Customer signs up for the platform", 1);

    Console.WriteLine("📥 Input: subscrio.Customers.CreateCustomerAsync(new CreateCustomerDto(");
    Console.WriteLine("  Key: \"acme-corp\",");
    Console.WriteLine("  DisplayName: \"Acme Corporation\",");
    Console.WriteLine("  Email: \"admin@acme-corp.com\"");
    Console.WriteLine("))");

    var customer = await subscrio.Customers.CreateCustomerAsync(new CreateCustomerDto(
        Key: "acme-corp",
        DisplayName: "Acme Corporation",
        Email: "admin@acme-corp.com"
    ));
    PrintSuccess($"Customer created: {customer.DisplayName} ({customer.Key})");

    // Verify creation by fetching the customer
    Console.WriteLine("📥 Input: subscrio.Customers.GetCustomerAsync(\"acme-corp\")");
    var fetchedCustomer = await subscrio.Customers.GetCustomerAsync(customer.Key);
    Console.WriteLine($"📄 Output: Customer DTO: {JsonSerializer.Serialize(fetchedCustomer, JsonOptions)}");
    PrintDivider();

    if (isInteractiveMode)
    {
        await PromptForDatabaseInspectionAsync("Phase 2: Customer Onboarding", "Step 1: Customer Creation Complete");
    }

    await SleepAsync(500);

    // Step 2: Create trial subscription
    PrintStep(2, "Create Trial Subscription");
    PrintInfo("Customer starts with a 14-day trial on the starter plan", 1);

    var trialEnd = DateTime.UtcNow.AddDays(14);

    Console.WriteLine("📥 Input: subscrio.Subscriptions.CreateSubscriptionAsync(new CreateSubscriptionDto(");
    Console.WriteLine("  CustomerKey: \"acme-corp\",");
    Console.WriteLine("  BillingCycleKey: \"starter-monthly\",");
    Console.WriteLine("  Key: \"acme-subscription\",");
    Console.WriteLine($"  TrialEndDate: {trialEnd:O}");
    Console.WriteLine("))");

    var subscription = await subscrio.Subscriptions.CreateSubscriptionAsync(new CreateSubscriptionDto(
        Key: "acme-subscription",
        CustomerKey: customer.Key,
        BillingCycleKey: "starter-monthly",
        TrialEndDate: trialEnd
    ));
    PrintSuccess($"Trial subscription created: {subscription.Key}");

    // Verify creation by fetching the subscription
    Console.WriteLine("📥 Input: subscrio.Subscriptions.GetSubscriptionAsync(\"acme-subscription\")");
    var fetchedSubscription = await subscrio.Subscriptions.GetSubscriptionAsync(subscription.Key);
    Console.WriteLine($"📄 Output: Trial Subscription DTO: {JsonSerializer.Serialize(fetchedSubscription, JsonOptions)}");

    PrintInfo($"Plan: {subscription.PlanKey}", 1);
    PrintInfo($"Status: {subscription.Status}", 1);
    PrintInfo($"Trial ends: {trialEnd:yyyy-MM-dd}", 1);
    PrintDivider();

    if (isInteractiveMode)
    {
        await PromptForDatabaseInspectionAsync("Phase 2: Customer Onboarding", "Step 2: Trial Subscription Creation Complete");
    }

    await SleepAsync(500);

    // Step 3: Check feature access
    PrintStep(3, "Check Feature Access");
    PrintInfo("Verify customer has access to starter plan features", 1);

    var customerKey = customer.Key;

    // Check numeric features
    var maxProjects = await subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
        customerKey,
        "projecthub",
        "max-projects"
    );
    var maxUsers = await subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
        customerKey,
        "projecthub",
        "max-users-per-project"
    );

    PrintSuccess($"Max projects: {maxProjects}");
    PrintSuccess($"Max users per project: {maxUsers}");

    // Check toggle features
    var hasGanttCharts = await subscrio.FeatureChecker.IsEnabledForCustomerAsync(
        customerKey,
        "projecthub",
        "gantt-charts"
    );
    var hasCustomBranding = await subscrio.FeatureChecker.IsEnabledForCustomerAsync(
        customerKey,
        "projecthub",
        "custom-branding"
    );
    var hasApiAccess = await subscrio.FeatureChecker.IsEnabledForCustomerAsync(
        customerKey,
        "projecthub",
        "api-access"
    );

    PrintInfo($"Gantt charts: {(hasGanttCharts ? "Enabled" : "Disabled")}", 1);
    PrintInfo($"Custom branding: {(hasCustomBranding ? "Enabled" : "Disabled")}", 1);
    PrintInfo($"API access: {(hasApiAccess ? "Enabled" : "Disabled")}", 1);

    // Show resolved feature values
    Console.WriteLine($"📄 Resolved Feature Values: {JsonSerializer.Serialize(new { maxProjects, maxUsers, hasGanttCharts, hasCustomBranding, hasApiAccess }, JsonOptions)}");

    PrintDivider();

    if (isInteractiveMode)
    {
        await PromptForDatabaseInspectionAsync("Phase 2: Customer Onboarding", "Step 3: Feature Access Verification Complete");
    }

    await SleepAsync(500);
}

async Task RunPhase3_TrialToPurchaseAsync(SubscrioInstance subscrio)
{
    PrintPhase(3, "Trial to Purchase");

    // Step 1: Trial converts to paid subscription
    PrintStep(1, "Trial Converts to Paid Subscription");
    PrintInfo("Customer decides to continue after trial period", 1);

    // Convert the trial subscription to active (trial conversion)
    Console.WriteLine("📥 Input: subscrio.Subscriptions.UpdateSubscriptionAsync(\"acme-subscription\", new UpdateSubscriptionDto(");
    Console.WriteLine("  TrialEndDate: null");
    Console.WriteLine("))");

    await subscrio.Subscriptions.UpdateSubscriptionAsync("acme-subscription", new UpdateSubscriptionDto(
        TrialEndDate: null // Clear trial end date to convert to active
    ));

    PrintSuccess("Trial subscription converted to active paid subscription");

    // Verify the conversion by fetching the updated subscription
    Console.WriteLine("📥 Input: subscrio.Subscriptions.GetSubscriptionAsync(\"acme-subscription\")");
    var convertedSubscription = await subscrio.Subscriptions.GetSubscriptionAsync("acme-subscription");
    Console.WriteLine($"📄 Output: Converted Subscription DTO: {JsonSerializer.Serialize(convertedSubscription, JsonOptions)}");

    PrintDivider();

    if (isInteractiveMode)
    {
        await PromptForDatabaseInspectionAsync("Phase 3: Trial to Purchase", "Step 1: Trial Conversion Complete");
    }

    await SleepAsync(500);
}

async Task RunPhase4_PlanUpgradeAsync(SubscrioInstance subscrio)
{
    PrintPhase(4, "Plan Upgrade");

    // Step 1: Upgrade existing subscription to professional plan
    PrintStep(1, "Upgrade to Professional Plan");
    PrintInfo("Customer upgrades from starter to professional plan", 1);

    Console.WriteLine("📥 Input: subscrio.Subscriptions.UpdateSubscriptionAsync(\"acme-subscription\", new UpdateSubscriptionDto(");
    Console.WriteLine("  BillingCycleKey: \"professional-monthly\"");
    Console.WriteLine("))");

    await subscrio.Subscriptions.UpdateSubscriptionAsync("acme-subscription", new UpdateSubscriptionDto(
        BillingCycleKey: "professional-monthly" // This will automatically update the plan
    ));

    PrintSuccess("Subscription upgraded to professional plan");

    // Verify the upgrade by fetching the updated subscription
    Console.WriteLine("📥 Input: subscrio.Subscriptions.GetSubscriptionAsync(\"acme-subscription\")");
    var upgradedSubscription = await subscrio.Subscriptions.GetSubscriptionAsync("acme-subscription");
    if (upgradedSubscription != null)
    {
        Console.WriteLine($"📄 Output: Upgraded Subscription DTO: {JsonSerializer.Serialize(upgradedSubscription, JsonOptions)}");
        PrintInfo($"Plan: {upgradedSubscription.PlanKey}", 1);
        PrintInfo($"Status: {upgradedSubscription.Status}", 1);
    }

    PrintDivider();

    if (isInteractiveMode)
    {
        await PromptForDatabaseInspectionAsync("Phase 4: Plan Upgrade", "Step 1: Plan Upgraded");
    }

    await SleepAsync(500);
}

async Task RunPhase5_FeatureOverridesAsync(SubscrioInstance subscrio)
{
    PrintPhase(5, "Feature Overrides");

    // Step 1: Add temporary override
    PrintStep(1, "Add Temporary Override");
    PrintInfo("Customer requests temporary increase in project limit", 1);

    Console.WriteLine("📥 Input: subscrio.Subscriptions.AddFeatureOverrideAsync(");
    Console.WriteLine("  \"acme-subscription\",");
    Console.WriteLine("  \"max-projects\",");
    Console.WriteLine("  \"10\",");
    Console.WriteLine("  OverrideType.Temporary");
    Console.WriteLine(")");

    await subscrio.Subscriptions.AddFeatureOverrideAsync(
        "acme-subscription",
        "max-projects",
        "10", // Increase from 5 to 10
        OverrideType.Temporary
    );

    PrintSuccess("Added temporary override: max-projects = 10");

    // Show the feature override that was added
    Console.WriteLine($"📄 Output: Feature Override Added: {JsonSerializer.Serialize(new { subscriptionKey = "acme-subscription", featureKey = "max-projects", value = "10", type = "temporary", description = "Temporary increase in project limit" }, JsonOptions)}");

    // Verify the override
    var maxProjects = await subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
        "acme-corp",
        "projecthub",
        "max-projects"
    );
    PrintInfo($"Current max projects: {maxProjects}", 1);

    PrintDivider();

    if (isInteractiveMode)
    {
        await PromptForDatabaseInspectionAsync("Phase 5: Feature Overrides", "Step 1: Temporary Override Added");
    }

    await SleepAsync(500);

    // Step 2: Add permanent override
    PrintStep(2, "Add Permanent Override");
    PrintInfo("Customer purchases add-on for Gantt charts", 1);

    Console.WriteLine("📥 Input: subscrio.Subscriptions.AddFeatureOverrideAsync(");
    Console.WriteLine("  \"acme-subscription\",");
    Console.WriteLine("  \"gantt-charts\",");
    Console.WriteLine("  \"true\",");
    Console.WriteLine("  OverrideType.Permanent");
    Console.WriteLine(")");

    await subscrio.Subscriptions.AddFeatureOverrideAsync(
        "acme-subscription",
        "gantt-charts",
        "true",
        OverrideType.Permanent
    );

    PrintSuccess("Added permanent override: gantt-charts = true");

    // Show the feature override that was added
    Console.WriteLine($"📄 Output: Feature Override Added: {JsonSerializer.Serialize(new { subscriptionKey = "acme-subscription", featureKey = "gantt-charts", value = "true", type = "permanent", description = "Permanent add-on for Gantt charts" }, JsonOptions)}");

    // Verify the override
    var hasGanttCharts = await subscrio.FeatureChecker.IsEnabledForCustomerAsync(
        "acme-corp",
        "projecthub",
        "gantt-charts"
    );
    PrintInfo($"Gantt charts enabled: {hasGanttCharts}", 1);

    PrintDivider();

    if (isInteractiveMode)
    {
        await PromptForDatabaseInspectionAsync("Phase 5: Feature Overrides", "Step 2: Permanent Override Added");
    }

    await SleepAsync(500);
}

async Task RunPhase6_SubscriptionRenewalAsync(SubscrioInstance subscrio)
{
    PrintPhase(6, "Subscription Renewal");

    // Step 1: Process subscription renewal
    PrintStep(1, "Process Subscription Renewal");
    PrintInfo("Subscription renews - temporary overrides are cleared, permanent ones remain", 1);

    Console.WriteLine("📥 Input: subscrio.Subscriptions.ClearTemporaryOverridesAsync(\"acme-subscription\")");
    await subscrio.Subscriptions.ClearTemporaryOverridesAsync("acme-subscription");
    PrintSuccess("Temporary overrides cleared during renewal");

    // Get the renewed subscription
    Console.WriteLine("📥 Input: subscrio.Subscriptions.GetSubscriptionAsync(\"acme-subscription\")");
    var renewedSubscription = await subscrio.Subscriptions.GetSubscriptionAsync("acme-subscription");
    Console.WriteLine($"📄 Output: Renewed Subscription DTO: {JsonSerializer.Serialize(renewedSubscription, JsonOptions)}");

    // Check feature resolution after renewal
    var maxProjects = await subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
        "acme-corp",
        "projecthub",
        "max-projects"
    );
    var hasGanttCharts = await subscrio.FeatureChecker.IsEnabledForCustomerAsync(
        "acme-corp",
        "projecthub",
        "gantt-charts"
    );

    PrintInfo($"Max projects: {maxProjects} (back to plan default)", 1);
    PrintInfo($"Gantt charts: {(hasGanttCharts ? "Enabled" : "Disabled")} (permanent override remains)", 1);

    PrintDivider();

    if (isInteractiveMode)
    {
        await PromptForDatabaseInspectionAsync("Phase 6: Subscription Renewal", "Step 1: Renewal Processed");
    }

    await SleepAsync(500);
}

async Task RunPhase7_DowngradeToFreeAsync(SubscrioInstance subscrio)
{
    PrintPhase(7, "Customer Cancellation and Downgrade");

    // Step 1: Customer cancels subscription
    PrintStep(1, "Customer Cancels Subscription");
    PrintInfo("Customer cancels professional subscription - status changes to cancellation_pending but remains active until period end", 1);

    var cancellationDate = DateTime.UtcNow;
    Console.WriteLine("📥 Input: subscrio.Subscriptions.UpdateSubscriptionAsync(\"acme-subscription\", new UpdateSubscriptionDto(");
    Console.WriteLine($"  CancellationDate: {cancellationDate:O}");
    Console.WriteLine("))");

    await subscrio.Subscriptions.UpdateSubscriptionAsync("acme-subscription", new UpdateSubscriptionDto(
        CancellationDate: cancellationDate
    ));
    PrintSuccess("Professional subscription cancelled by customer");

    // Verify the cancelled subscription
    Console.WriteLine("📥 Input: subscrio.Subscriptions.GetSubscriptionAsync(\"acme-subscription\")");
    var cancelledSubscription = await subscrio.Subscriptions.GetSubscriptionAsync("acme-subscription");
    if (cancelledSubscription != null)
    {
        Console.WriteLine($"📄 Output: Cancelled Subscription DTO: {JsonSerializer.Serialize(cancelledSubscription, JsonOptions)}");
        PrintInfo($"Status: {cancelledSubscription.Status}", 1);
        PrintInfo($"Cancellation date: {(cancelledSubscription.CancellationDate != null ? DateTime.Parse(cancelledSubscription.CancellationDate).ToString("yyyy-MM-dd") : "N/A")}", 1);
        PrintInfo($"Current period end: {(cancelledSubscription.CurrentPeriodEnd != null ? DateTime.Parse(cancelledSubscription.CurrentPeriodEnd).ToString("yyyy-MM-dd") : "N/A")}", 1);
        PrintInfo("Note: Subscription remains active until period end date (cancellation_pending status)", 1);
    }

    PrintDivider();

    if (isInteractiveMode)
    {
        await PromptForDatabaseInspectionAsync("Phase 7: Customer Cancellation", "Step 1: Subscription Cancelled");
    }

    await SleepAsync(500);

    // Step 2: Customer opts into the free plan manually after cancellation
    PrintStep(2, "Customer Starts Free Plan");
    PrintInfo("After the paid plan lapses, the customer opts into the free tier manually", 1);

    Console.WriteLine("📥 Input: subscrio.Subscriptions.CreateSubscriptionAsync(new CreateSubscriptionDto(");
    Console.WriteLine("  CustomerKey: \"acme-corp\",");
    Console.WriteLine("  BillingCycleKey: \"free-forever\",");
    Console.WriteLine("  Key: \"acme-free-subscription\"");
    Console.WriteLine("))");

    var freeSubscription = await subscrio.Subscriptions.CreateSubscriptionAsync(new CreateSubscriptionDto(
        Key: "acme-free-subscription",
        CustomerKey: "acme-corp",
        BillingCycleKey: "free-forever"
    ));
    PrintSuccess("Free plan subscription created for the customer");

    Console.WriteLine($"📄 Output: Free Subscription DTO: {JsonSerializer.Serialize(freeSubscription, JsonOptions)}");

    PrintDivider();

    if (isInteractiveMode)
    {
        await PromptForDatabaseInspectionAsync("Phase 7: Customer Downgrade", "Step 2: Free Plan Subscription Created");
    }

    await SleepAsync(500);

    // Step 3: Check free plan feature access
    PrintStep(3, "Check Free Plan Feature Access");
    PrintInfo("Verify customer has access to free plan features after downgrading", 1);

    var maxProjects = await subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
        "acme-corp",
        "projecthub",
        "max-projects"
    );
    var maxUsers = await subscrio.FeatureChecker.GetValueForCustomerAsync<string>(
        "acme-corp",
        "projecthub",
        "max-users-per-project"
    );
    var hasGanttCharts = await subscrio.FeatureChecker.IsEnabledForCustomerAsync(
        "acme-corp",
        "projecthub",
        "gantt-charts"
    );
    var hasCustomBranding = await subscrio.FeatureChecker.IsEnabledForCustomerAsync(
        "acme-corp",
        "projecthub",
        "custom-branding"
    );
    var hasApiAccess = await subscrio.FeatureChecker.IsEnabledForCustomerAsync(
        "acme-corp",
        "projecthub",
        "api-access"
    );

    PrintSuccess($"Max projects: {maxProjects} (free plan limit)");
    PrintSuccess($"Max users per project: {maxUsers} (free plan limit)");
    PrintInfo($"Gantt charts: {(hasGanttCharts ? "Enabled" : "Disabled")} (not available on free plan)", 1);
    PrintInfo($"Custom branding: {(hasCustomBranding ? "Enabled" : "Disabled")} (not available on free plan)", 1);
    PrintInfo($"API access: {(hasApiAccess ? "Enabled" : "Disabled")} (not available on free plan)", 1);

    // Show resolved feature values
    Console.WriteLine($"📄 Output: Resolved Feature Values (Free Plan): {JsonSerializer.Serialize(new { maxProjects, maxUsers, hasGanttCharts, hasCustomBranding, hasApiAccess }, JsonOptions)}");

    PrintDivider();

    if (isInteractiveMode)
    {
        await PromptForDatabaseInspectionAsync("Phase 7: Customer Downgrade", "Step 3: Free Plan Features Verified");
    }

    await SleepAsync(500);
}

async Task RunPhase8_SummaryAsync()
{
    PrintPhase(7, "Summary");

    Console.WriteLine("🎉 Demo completed successfully!");
    Console.WriteLine("");
    Console.WriteLine("This demo showcased a realistic subscription lifecycle:");
    Console.WriteLine("• Product and feature management");
    Console.WriteLine("• Plan configuration with feature values");
    Console.WriteLine("• Customer onboarding with trial subscription");
    Console.WriteLine("• Trial conversion to paid subscription");
    Console.WriteLine("• Plan upgrades within the same subscription");
    Console.WriteLine("• Feature overrides (temporary and permanent)");
    Console.WriteLine("• Subscription renewal and override lifecycle");
    Console.WriteLine("• Customer-driven downgrade path back to the free plan");
    Console.WriteLine("• Feature resolution hierarchy (override > plan > default)");
    Console.WriteLine("• Billing cycle management");
    Console.WriteLine("");
    Console.WriteLine("Key takeaways:");
    Console.WriteLine("• Subscrio handles realistic subscription scenarios elegantly");
    Console.WriteLine("• Feature overrides provide flexibility for custom needs");
    Console.WriteLine("• Temporary overrides clear on renewal, permanent ones persist");
    Console.WriteLine("• The API supports complete subscription lifecycle management");
    Console.WriteLine("");
}

// ═══════════════════════════════════════════════════════════
// HELPER METHODS
// ═══════════════════════════════════════════════════════════

string ExtractDatabaseName(string connectionString)
{
    try
    {
        // Parse PostgreSQL connection string
        // Format: Host=localhost;Port=5432;Database=subscrio_demo;Username=postgres;Password=postgres
        var parts = connectionString.Split(';');
        var dbPart = parts.FirstOrDefault(p => p.StartsWith("Database=", StringComparison.OrdinalIgnoreCase));
        if (dbPart != null)
        {
            return dbPart.Split('=')[1];
        }
        return "unknown";
    }
    catch
    {
        return "unknown";
    }
}

void PrintHeader(string connectionString)
{
    var dbName = ExtractDatabaseName(connectionString);

    Console.WriteLine("\n╔═══════════════════════════════════════════════════════════╗");
    Console.WriteLine("║                                                           ║");
    Console.WriteLine("║         Subscrio Customer Lifecycle Demo                  ║");
    Console.WriteLine("║         Scenario: ProjectHub SaaS Platform                ║");
    Console.WriteLine("║                                                           ║");
    Console.WriteLine($"║         Database: {dbName.PadRight(39)} ║");
    Console.WriteLine("║                                                           ║");
    if (isInteractiveMode)
    {
        Console.WriteLine("║         🔍 INTERACTIVE MODE ENABLED                      ║");
        Console.WriteLine("║         (Pause after each step for database inspection) ║");
        Console.WriteLine("║                                                           ║");
    }
    Console.WriteLine("╚═══════════════════════════════════════════════════════════╝\n");
}

void PrintPhase(int phaseNum, string title)
{
    Console.WriteLine("\n" + new string('═', 63));
    Console.WriteLine($"  PHASE {phaseNum}: {title}");
    Console.WriteLine(new string('═', 63) + "\n");
}

void PrintStep(int stepNum, string title)
{
    Console.WriteLine($"\n┌─ Step {stepNum}: {title}");
    Console.WriteLine("│");
}

void PrintSuccess(string message)
{
    Console.WriteLine($"│ ✓ {message}");
}

void PrintInfo(string message, int indent = 1)
{
    var prefix = "│" + new string(' ', indent * 2);
    Console.WriteLine($"{prefix}{message}");
}

void PrintDivider()
{
    Console.WriteLine("│");
    Console.WriteLine("└" + new string('─', 61));
}

Task SleepAsync(int ms)
{
    return Task.Delay(ms);
}

async Task PromptForDatabaseInspectionAsync(string phase, string step)
{
    if (!isInteractiveMode) return;

    Console.WriteLine("\n" + new string('─', 63));
    Console.WriteLine($"🔍 INTERACTIVE MODE: {phase} - {step}");
    Console.WriteLine(new string('─', 63));
    Console.WriteLine("Database state paused for inspection.");
    Console.WriteLine("Check your database to see the current state.");
    Console.WriteLine("Press ENTER to continue...");

    await Task.Run(() => Console.ReadLine());

    Console.WriteLine("Continuing...\n");
}

async Task<string> PromptDemoStartAsync(bool automated = false)
{
    if (automated)
    {
        Console.WriteLine("\n🤖 RUNNING IN AUTOMATED MODE");
        Console.WriteLine(new string('═', 50));
        Console.WriteLine("This demo will delete existing demo entities and then create the following entities:");
        Console.WriteLine("");
        Console.WriteLine("📦 PRODUCTS: projecthub");
        Console.WriteLine("🔧 FEATURES: max-projects, max-users-per-project, gantt-charts, custom-branding, api-access");
        Console.WriteLine("📋 PLANS: free, starter, professional, enterprise");
        Console.WriteLine("💳 BILLING CYCLES: free-forever, starter-monthly, starter-annual, professional-monthly, professional-annual, enterprise-monthly, enterprise-annual");
        Console.WriteLine("👤 CUSTOMERS: acme-corp");
        Console.WriteLine("🔄 SUBSCRIPTION LIFECYCLE: trial → purchase → upgrade → renewal → cancellation → downgrade");
        Console.WriteLine("");
        Console.WriteLine("⚠️  Please ensure you have a dedicated test database or");
        Console.WriteLine("   are prepared to manually clean up these entities after the demo.");
        Console.WriteLine("");
        Console.WriteLine("🚀 Starting demo automatically...\n");
        return "continue";
    }

    Console.WriteLine("\n⚠️  IMPORTANT: Database Cleanup Required");
    Console.WriteLine(new string('═', 50));
    Console.WriteLine("This demo will delete existing demo entities and then create the following entities:");
    Console.WriteLine("");
    Console.WriteLine("📦 PRODUCTS: projecthub");
    Console.WriteLine("🔧 FEATURES: max-projects, max-users-per-project, gantt-charts, custom-branding, api-access");
    Console.WriteLine("📋 PLANS: free, starter, professional, enterprise");
    Console.WriteLine("💳 BILLING CYCLES: free-forever, starter-monthly, starter-annual, professional-monthly, professional-annual, enterprise-monthly, enterprise-annual");
    Console.WriteLine("👤 CUSTOMERS: acme-corp");
    Console.WriteLine("🔄 SUBSCRIPTION LIFECYCLE: trial → purchase → upgrade → renewal → cancellation → downgrade");
    Console.WriteLine("");
    Console.WriteLine("⚠️  Please ensure you have a dedicated test database or");
    Console.WriteLine("   are prepared to manually clean up these entities after the demo.");
    Console.WriteLine("");
    Console.WriteLine("Options:");
    Console.WriteLine("  [ENTER] Continue with demo");
    Console.WriteLine("  [q] Quit");
    Console.WriteLine("  [i] Continue in interactive mode (pause after each step)");
    Console.WriteLine("");
    Console.Write("Your choice: ");

    var input = Console.ReadLine()?.Trim().ToLowerInvariant() ?? "";
    return input;
}

async Task CleanupDemoEntitiesAsync(SubscrioInstance subscrio)
{
    // Access the private _db field via reflection (similar to TypeScript's (subscrio as any).db)
    var dbField = typeof(SubscrioInstance).GetField("_db", BindingFlags.NonPublic | BindingFlags.Instance);
    if (dbField == null)
    {
        Console.WriteLine("⚠️  Warning: Could not access database context for cleanup");
        return;
    }

    var db = (SubscrioDbContext)dbField.GetValue(subscrio)!;

    Console.WriteLine("🧹 Cleaning up existing demo entities...");

    // Helper function to safely delete (suppresses errors for missing tables or 0 rows)
    async Task SafeDeleteAsync(string sql, string description)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql);
        }
        catch (Exception error)
        {
            // Suppress errors for expected scenarios:
            // - Table doesn't exist (--recreate case)
            // - 0 rows affected (empty tables, normal case)
            var errorString = error.ToString().ToLowerInvariant();
            var isExpected =
                errorString.Contains("does not exist") ||
                errorString.Contains("relation") ||
                errorString.Contains("0 rows") ||
                errorString.Contains("no rows");

            if (!isExpected)
            {
                // Only log unexpected errors
                Console.WriteLine($"⚠️  Warning: {description} failed: {error.Message}");
            }
        }
    }

    // Delete in reverse dependency order
    // Note: These may affect 0 rows or fail if tables don't exist (when using --recreate)
    Console.WriteLine("🗑️  Deleting demo entities...");
    await SafeDeleteAsync(
        "DELETE FROM subscrio.subscriptions WHERE key IN ('acme-subscription', 'acme-free-subscription')",
        "Deleting subscriptions"
    );
    await SafeDeleteAsync("DELETE FROM subscrio.customers WHERE key = 'acme-corp'", "Deleting customers");
    await SafeDeleteAsync(
        "DELETE FROM subscrio.billing_cycles WHERE key IN ('free-forever', 'starter-monthly', 'starter-annual', 'professional-monthly', 'professional-annual', 'enterprise-monthly', 'enterprise-annual')",
        "Deleting billing cycles"
    );
    await SafeDeleteAsync(
        "DELETE FROM subscrio.plans WHERE key IN ('free', 'starter', 'professional', 'enterprise')",
        "Deleting plans"
    );
    await SafeDeleteAsync(
        "DELETE FROM subscrio.features WHERE key IN ('max-projects', 'max-users-per-project', 'gantt-charts', 'custom-branding', 'api-access')",
        "Deleting features"
    );
    await SafeDeleteAsync("DELETE FROM subscrio.products WHERE key = 'projecthub'", "Deleting products");

    Console.WriteLine("✅ Demo entities cleanup completed");
    Console.WriteLine(new string('═', 50) + "\n");
}


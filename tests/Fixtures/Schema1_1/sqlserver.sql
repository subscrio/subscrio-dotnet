-- Historical EF schema from subscrio-dotnet commit ab3c851 (before entitlement changes).
IF SCHEMA_ID(N'subscrio') IS NULL EXEC(N'CREATE SCHEMA [subscrio];');
GO


CREATE TABLE [subscrio].[customers] (
    [id] bigint NOT NULL IDENTITY,
    [key] nvarchar(450) NOT NULL,
    [display_name] nvarchar(max) NULL,
    [email] nvarchar(max) NULL,
    [external_billing_id] nvarchar(450) NULL,
    [status] nvarchar(max) NOT NULL,
    [metadata] nvarchar(max) NULL,
    [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_customers] PRIMARY KEY ([id])
);
GO


CREATE TABLE [subscrio].[features] (
    [id] bigint NOT NULL IDENTITY,
    [key] nvarchar(450) NOT NULL,
    [display_name] nvarchar(max) NOT NULL,
    [description] nvarchar(max) NULL,
    [value_type] nvarchar(max) NOT NULL,
    [default_value] nvarchar(max) NOT NULL,
    [group_name] nvarchar(max) NULL,
    [status] nvarchar(max) NOT NULL,
    [validator] nvarchar(max) NULL,
    [metadata] nvarchar(max) NULL,
    [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_features] PRIMARY KEY ([id])
);
GO


CREATE TABLE [subscrio].[products] (
    [id] bigint NOT NULL IDENTITY,
    [key] nvarchar(450) NOT NULL,
    [display_name] nvarchar(max) NOT NULL,
    [description] nvarchar(max) NULL,
    [status] nvarchar(max) NOT NULL,
    [metadata] nvarchar(max) NULL,
    [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_products] PRIMARY KEY ([id])
);
GO


CREATE TABLE [subscrio].[system_config] (
    [id] bigint NOT NULL IDENTITY,
    [config_key] nvarchar(450) NOT NULL,
    [config_value] nvarchar(max) NOT NULL,
    [encrypted] bit NOT NULL DEFAULT CAST(0 AS bit),
    [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_system_config] PRIMARY KEY ([id])
);
GO


CREATE TABLE [subscrio].[product_features] (
    [id] bigint NOT NULL IDENTITY,
    [product_id] bigint NOT NULL,
    [feature_id] bigint NOT NULL,
    [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_product_features] PRIMARY KEY ([id]),
    CONSTRAINT [FK_product_features_features_feature_id] FOREIGN KEY ([feature_id]) REFERENCES [subscrio].[features] ([id]),
    CONSTRAINT [FK_product_features_products_product_id] FOREIGN KEY ([product_id]) REFERENCES [subscrio].[products] ([id])
);
GO


CREATE TABLE [subscrio].[billing_cycles] (
    [id] bigint NOT NULL IDENTITY,
    [plan_id] bigint NOT NULL,
    [key] nvarchar(450) NOT NULL,
    [display_name] nvarchar(max) NOT NULL,
    [description] nvarchar(max) NULL,
    [status] nvarchar(max) NOT NULL DEFAULT N'active',
    [duration_value] int NULL,
    [duration_unit] nvarchar(max) NOT NULL,
    [external_product_id] nvarchar(max) NULL,
    [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_billing_cycles] PRIMARY KEY ([id])
);
GO


CREATE TABLE [subscrio].[plans] (
    [id] bigint NOT NULL IDENTITY,
    [product_id] bigint NOT NULL,
    [key] nvarchar(450) NOT NULL,
    [display_name] nvarchar(max) NOT NULL,
    [description] nvarchar(max) NULL,
    [status] nvarchar(max) NOT NULL,
    [on_expire_transition_to_billing_cycle_id] bigint NULL,
    [metadata] nvarchar(max) NULL,
    [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_plans] PRIMARY KEY ([id]),
    CONSTRAINT [FK_plans_billing_cycles_on_expire_transition_to_billing_cycle_id] FOREIGN KEY ([on_expire_transition_to_billing_cycle_id]) REFERENCES [subscrio].[billing_cycles] ([id]),
    CONSTRAINT [FK_plans_products_product_id] FOREIGN KEY ([product_id]) REFERENCES [subscrio].[products] ([id])
);
GO


CREATE TABLE [subscrio].[plan_features] (
    [id] bigint NOT NULL IDENTITY,
    [plan_id] bigint NOT NULL,
    [feature_id] bigint NOT NULL,
    [value] nvarchar(max) NOT NULL,
    [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_plan_features] PRIMARY KEY ([id]),
    CONSTRAINT [FK_plan_features_features_feature_id] FOREIGN KEY ([feature_id]) REFERENCES [subscrio].[features] ([id]),
    CONSTRAINT [FK_plan_features_plans_plan_id] FOREIGN KEY ([plan_id]) REFERENCES [subscrio].[plans] ([id])
);
GO


CREATE TABLE [subscrio].[subscriptions] (
    [id] bigint NOT NULL IDENTITY,
    [key] nvarchar(450) NOT NULL,
    [customer_id] bigint NOT NULL,
    [plan_id] bigint NOT NULL,
    [billing_cycle_id] bigint NOT NULL,
    [activation_date] datetime2 NULL,
    [expiration_date] datetime2 NULL,
    [cancellation_date] datetime2 NULL,
    [trial_end_date] datetime2 NULL,
    [current_period_start] datetime2 NULL,
    [current_period_end] datetime2 NULL,
    [stripe_subscription_id] nvarchar(450) NULL,
    [metadata] nvarchar(max) NULL,
    [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [updated_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    [is_archived] bit NOT NULL DEFAULT CAST(0 AS bit),
    [transitioned_at] datetime2 NULL,
    CONSTRAINT [PK_subscriptions] PRIMARY KEY ([id]),
    CONSTRAINT [FK_subscriptions_billing_cycles_billing_cycle_id] FOREIGN KEY ([billing_cycle_id]) REFERENCES [subscrio].[billing_cycles] ([id]),
    CONSTRAINT [FK_subscriptions_customers_customer_id] FOREIGN KEY ([customer_id]) REFERENCES [subscrio].[customers] ([id]),
    CONSTRAINT [FK_subscriptions_plans_plan_id] FOREIGN KEY ([plan_id]) REFERENCES [subscrio].[plans] ([id])
);
GO


CREATE TABLE [subscrio].[subscription_feature_overrides] (
    [id] bigint NOT NULL IDENTITY,
    [subscription_id] bigint NOT NULL,
    [feature_id] bigint NOT NULL,
    [value] nvarchar(max) NOT NULL,
    [override_type] nvarchar(max) NOT NULL,
    [created_at] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT [PK_subscription_feature_overrides] PRIMARY KEY ([id]),
    CONSTRAINT [FK_subscription_feature_overrides_features_feature_id] FOREIGN KEY ([feature_id]) REFERENCES [subscrio].[features] ([id]),
    CONSTRAINT [FK_subscription_feature_overrides_subscriptions_subscription_id] FOREIGN KEY ([subscription_id]) REFERENCES [subscrio].[subscriptions] ([id])
);
GO


CREATE UNIQUE INDEX [IX_billing_cycles_plan_id_key] ON [subscrio].[billing_cycles] ([plan_id], [key]);
GO


CREATE UNIQUE INDEX [IX_customers_external_billing_id] ON [subscrio].[customers] ([external_billing_id]) WHERE external_billing_id IS NOT NULL;
GO


CREATE UNIQUE INDEX [IX_customers_key] ON [subscrio].[customers] ([key]);
GO


CREATE UNIQUE INDEX [IX_features_key] ON [subscrio].[features] ([key]);
GO


CREATE INDEX [IX_plan_features_feature_id] ON [subscrio].[plan_features] ([feature_id]);
GO


CREATE UNIQUE INDEX [IX_plan_features_plan_id_feature_id] ON [subscrio].[plan_features] ([plan_id], [feature_id]);
GO


CREATE INDEX [IX_plans_on_expire_transition_to_billing_cycle_id] ON [subscrio].[plans] ([on_expire_transition_to_billing_cycle_id]);
GO


CREATE UNIQUE INDEX [IX_plans_product_id_key] ON [subscrio].[plans] ([product_id], [key]);
GO


CREATE INDEX [IX_product_features_feature_id] ON [subscrio].[product_features] ([feature_id]);
GO


CREATE UNIQUE INDEX [IX_product_features_product_id_feature_id] ON [subscrio].[product_features] ([product_id], [feature_id]);
GO


CREATE UNIQUE INDEX [IX_products_key] ON [subscrio].[products] ([key]);
GO


CREATE INDEX [IX_subscription_feature_overrides_feature_id] ON [subscrio].[subscription_feature_overrides] ([feature_id]);
GO


CREATE UNIQUE INDEX [IX_subscription_feature_overrides_subscription_id_feature_id] ON [subscrio].[subscription_feature_overrides] ([subscription_id], [feature_id]);
GO


CREATE INDEX [IX_subscriptions_billing_cycle_id] ON [subscrio].[subscriptions] ([billing_cycle_id]);
GO


CREATE INDEX [IX_subscriptions_customer_id] ON [subscrio].[subscriptions] ([customer_id]);
GO


CREATE UNIQUE INDEX [IX_subscriptions_key] ON [subscrio].[subscriptions] ([key]);
GO


CREATE INDEX [IX_subscriptions_plan_id] ON [subscrio].[subscriptions] ([plan_id]);
GO


CREATE UNIQUE INDEX [IX_subscriptions_stripe_subscription_id] ON [subscrio].[subscriptions] ([stripe_subscription_id]) WHERE stripe_subscription_id IS NOT NULL;
GO


CREATE UNIQUE INDEX [IX_system_config_config_key] ON [subscrio].[system_config] ([config_key]);
GO


ALTER TABLE [subscrio].[billing_cycles] ADD CONSTRAINT [FK_billing_cycles_plans_plan_id] FOREIGN KEY ([plan_id]) REFERENCES [subscrio].[plans] ([id]);
GO

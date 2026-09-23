# Changelog

All notable changes to Subscrio.Core are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.5.0] - 2026-09-23

### Added

- Product add-ons with feature values, subscription quantities, and additive or replacement behavior.
- Rules for combining plan and add-on values within a subscription and allowances across subscriptions, with diagnostic explanations.
- Metered features with calendar or billing-period resets, hard and soft limits, usage history, and idempotent reporting.
- Customer credit wallets with grants, feature consumption costs, scheduled plan grants, expiration, and a transaction ledger.
- Timed subscription feature overrides that stop applying at their expiration time.
- Schema migrations through version 1.4.0 for PostgreSQL and SQL Server, including historical-upgrade and cross-language accounting verification.

### Changed

- Feature, product, plan, and subscription results include their related add-on data.
- Feature create/update includes metered configuration; product-feature association accepts value calculation rules.
- Configuration sync and export include add-ons, metering settings, credit rules, and subscription overrides.
- Hooks cover add-on attachments, usage reporting, and credit operations. Console demos cover the new capabilities.

### Upgrade

- Back up existing databases and run `MigrateAsync()` before using the new capabilities. Upgrade applications sharing a database together.
- Existing product-feature associations retain their previous value-selection behavior. Explicitly configure additive rules when enabling extra-capacity add-ons.
- This is a core-only release. Extension and integration packages are not released at 0.5.0. Validate those packages separately before upgrading applications that use them.

## [0.4.0] - 2026-09-20

### Added

- SQL Server schema installation and provider-specific subscription status views alongside PostgreSQL support.
- Stripe webhook verification through `StripeConfig.ConstructStripeEvent` and `WebhookSecret`.
- Explicit clear operations for subscription trial dates and plan expiration transitions.
- Subscription feature overrides on read paths.
- Admin-passphrase verification before destructive schema removal.
- The first `Subscrio.Abp` integration package, released at the same version as `Subscrio.Core`.
- Stripe received hooks now include optional customer and subscription IDs extracted from the verified event.

### Changed

- Configuration sync now loads every page and applies clearer validation across catalog records.
- Feature resolution evaluates every eligible subscription and converts stored values to the requested type.
- List queries apply ordering before pagination.
- Dependency injection registration no longer performs asynchronous catalog work during service registration.
- Stripe subscription creation remains outside Subscrio; use Checkout or verified webhook processing instead.

### Fixed

- Expiration transitions save the replacement subscription before archiving the old one.
- Before-hook mutations are validated again before customer and subscription records are saved.
- Plan feature values must belong to the plan's product.
- SQL Server schema installation retries transient failures and uses provider-specific SQL.
- Schema verification no longer reports unexpected database errors as a missing schema.
- Customer archive-before-delete failures consistently use `DomainException`.

## [0.3.1] - 2026-03-01

### Added

- Initial public NuGet packaging for plan-based entitlements, subscriptions, and optional Stripe event processing
- PostgreSQL and SQL Server support via EF Core

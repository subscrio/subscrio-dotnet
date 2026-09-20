# Changelog

All notable changes to Subscrio.Core are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

# Changelog

All notable changes to Subscrio.Core are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed

- Expire subscription transitions create the replacement before archiving the expired subscription
- Before-hook mutations on customer/subscription create paths are re-validated
- Plan feature values require the feature to be associated with the plan's product
- `OnExpireTransitionToBillingCycleKey` can be cleared via update / ConfigSync
- Subscription read paths load feature overrides instead of returning empty lists
- Toggle feature create validator accepts case-insensitive `true`/`false`
- Schema verify no longer treats all exceptions as "schema missing"
- SQL Server transient errors are retried during schema install
- Archive-before-delete consistently throws `DomainException` for customers

## [0.3.1] - 2026-03-01

### Added

- Initial public NuGet packaging for plan-based entitlements, subscriptions, and optional Stripe event processing
- PostgreSQL and SQL Server support via EF Core

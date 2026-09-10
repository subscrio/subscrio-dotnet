# Contributing

Thanks for contributing to Subscrio.Core.

## Hub and docs

- Product concepts and architecture: [subscrio/subscrio](https://github.com/subscrio/subscrio)
- API reference: [docs.subscrio.com](https://docs.subscrio.com)
- This package: [subscrio/subscrio-dotnet](https://github.com/subscrio/subscrio-dotnet)

## Prerequisites

- .NET 8+ SDK (library targets net8.0 / net9.0 / net10.0)
- PostgreSQL for E2E tests (see `tests/README.md`)

## Build and test

From the repo root (`core/dotnet`):

```bash
dotnet build
dotnet test tests/Subscrio.Core.Tests.csproj --filter "FullyQualifiedName~Unit"
```

E2E tests need a configured database; see `tests/README.md` and `tests/appsettings.json`.

```bash
dotnet test tests/Subscrio.Core.Tests.csproj
```

## Pull requests

- Keep changes focused and build-green.
- Prefer small PRs with a clear summary and test notes.
- Do not commit secrets (connection strings, Stripe keys, `.env` files).

## Code of conduct

See [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

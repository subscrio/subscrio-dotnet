# Quick Setup Guide for Running Tests

## Prerequisites

1. **PostgreSQL 15+** installed and running
2. **.NET 8.0 SDK** installed
3. **Database user** with CREATEDB privilege

## Setup Steps

### 1. Install PostgreSQL (if not already installed)

**Windows:**
- Download from https://www.postgresql.org/download/windows/
- Install PostgreSQL 15 or later
- Ensure PostgreSQL service is running (check Services app)

**macOS:**
```bash
brew install postgresql@15
brew services start postgresql@15
```

**Linux:**
```bash
sudo apt-get install postgresql-15
sudo systemctl start postgresql
```

### 2. Verify PostgreSQL is Running

**Windows:**
```powershell
# Check if service is running
Get-Service -Name postgresql*
```

**macOS/Linux:**
```bash
# Check if PostgreSQL is running
pg_isready
```

### 3. Configure Database Connection (Optional)

The tests use a default connection string. If your PostgreSQL setup is different, set the environment variable:

**PowerShell:**
```powershell
$env:TEST_DATABASE_URL = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=yourpassword"
```

**Bash:**
```bash
export TEST_DATABASE_URL="Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=yourpassword"
```

**Default connection string (if not set):**
```
Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres
```

### 4. Grant CREATEDB Privilege (if needed)

If you get "permission denied to create database" errors:

```sql
-- Connect to PostgreSQL
psql -U postgres

-- Grant CREATEDB privilege
ALTER USER postgres CREATEDB;
```

### 5. Run Tests

**From repository root:**
```powershell
cd .\tests
dotnet test
```

**Or from repository root:**
```powershell
dotnet test tests/Subscrio.Core.Tests.csproj
```

**Using the runsettings file (stops on first failure):**
```powershell
dotnet test --settings Subscrio.Core.Tests.runsettings
```

**Note:** The test project is configured to stop on first failure by default. This helps during development by immediately showing the first error instead of running all tests.

## What the Tests Do

1. **Create Test Database**: Tests automatically create a database named `subscrio_test`
2. **Install Schema**: The test database schema is installed automatically
3. **Run Tests**: All E2E tests run against the test database
4. **Cleanup**: Test database is automatically dropped after tests complete (unless `KEEP_TEST_DB=true`)

## Debug Mode

To keep the test database for inspection after tests:

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

After tests complete, you can connect to the database:
```bash
psql -h localhost -U postgres -d subscrio_test
```

## Troubleshooting

### "Connection refused"
- Ensure PostgreSQL service is running
- Check PostgreSQL is listening on port 5432
- Verify connection string credentials

### "Permission denied to create database"
- Grant CREATEDB privilege: `ALTER USER postgres CREATEDB;`

### "Database does not exist"
- The tests create the database automatically
- Ensure you can connect to the `postgres` database (used to create test DB)

### Tests hang or timeout
- Check for unclosed database connections
- Use `KEEP_TEST_DB=true` to inspect the database state
- Check PostgreSQL logs for errors

## Next Steps

Once tests are running, see [README.md](README.md) for:
- Detailed test structure
- Writing new tests
- Test coverage requirements
- CI/CD integration examples


using FluentAssertions;
using Subscrio.Core;
using Subscrio.Core.Application.DTOs;
using Subscrio.Core.Application.Errors;
using Subscrio.Core.Config;
using Subscrio.Core.Domain.ValueObjects;
using Subscrio.Core.Tests.Setup;
using System.Collections.Generic;
using Xunit;

namespace Subscrio.Core.Tests.E2E;

public class CustomersTests : IDisposable
{
    private readonly Subscrio _subscrio;
    private readonly TestFixtures _fixtures;

    public CustomersTests()
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
                PoolSize = 5, // Reduced pool size for tests
                DatabaseType = DatabaseType.PostgreSQL
            }
        };
        
        _subscrio = new Subscrio(config);
        _fixtures = new TestFixtures(_subscrio);
    }

    public void Dispose()
    {
        _subscrio?.Dispose();
    }

    public class CrudOperations : CustomersTests
    {
        public CrudOperations() : base() { }

        [Fact]
        public async Task CreatesCustomerWithValidData()
        {
            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Test Customer",
                ["Email"] = "test@example.com"
            });

            customer.Should().NotBeNull();
            customer.Key.Should().NotBeNullOrEmpty();
            customer.DisplayName.Should().Be("Test Customer");
            customer.Email.Should().Be("test@example.com");
            customer.Status.Should().Be("active");
            customer.CreatedAt.Should().NotBeNullOrEmpty();
            customer.UpdatedAt.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task RetrievesCustomerByKeyAfterCreation()
        {
            var created = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Retrieve Customer"
            });

            var retrieved = await _subscrio.Customers.GetCustomerAsync(created.Key);
            retrieved.Should().NotBeNull();
            retrieved!.Key.Should().Be(created.Key);
            retrieved.DisplayName.Should().Be(created.DisplayName);
        }

        [Fact]
        public async Task UpdatesCustomerDisplayName()
        {
            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Original Name"
            });

            var updated = await _subscrio.Customers.UpdateCustomerAsync(customer.Key, new UpdateCustomerDto(
                DisplayName: "Updated Name"
            ));

            updated.DisplayName.Should().Be("Updated Name");
            updated.Key.Should().Be(customer.Key);
        }

        [Fact]
        public async Task UpdatesCustomerEmail()
        {
            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["Email"] = "old@example.com"
            });

            var updated = await _subscrio.Customers.UpdateCustomerAsync(customer.Key, new UpdateCustomerDto(
                Email: "new@example.com"
            ));

            updated.Email.Should().Be("new@example.com");
        }

        [Fact]
        public async Task UpdatesCustomerExternalBillingId()
        {
            var customer = await _fixtures.CreateCustomerAsync();

            var billingId = $"cus_stripe_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var updated = await _subscrio.Customers.UpdateCustomerAsync(customer.Key, new UpdateCustomerDto(
                ExternalBillingId: billingId
            ));

            updated.ExternalBillingId.Should().Be(billingId);
        }

        [Fact]
        public async Task ReturnsNullForNonExistentCustomer()
        {
            var result = await _subscrio.Customers.GetCustomerAsync("non-existent-customer");
            result.Should().BeNull();
        }

        [Fact]
        public async Task ThrowsErrorWhenUpdatingNonExistentCustomer()
        {
            await Assert.ThrowsAsync<NotFoundException>(async () =>
                await _subscrio.Customers.UpdateCustomerAsync("non-existent-customer", new UpdateCustomerDto(
                    DisplayName: "New Name"
                ))
            );
        }
    }

    public class ValidationTests : CustomersTests
    {
        public ValidationTests() : base() { }

        [Fact]
        public async Task ThrowsErrorForEmptyCustomerKey()
        {
            await Assert.ThrowsAsync<ValidationException>(async () =>
                await _subscrio.Customers.CreateCustomerAsync(new CreateCustomerDto(
                    Key: "",
                    DisplayName: "Test"
                ))
            );
        }

        [Fact]
        public async Task ThrowsErrorForInvalidEmailFormat()
        {
            await Assert.ThrowsAsync<ValidationException>(async () =>
                await _subscrio.Customers.CreateCustomerAsync(new CreateCustomerDto(
                    Key: "invalid-email-customer",
                    Email: "not-an-email"
                ))
            );
        }

        [Fact]
        public async Task ThrowsErrorForDuplicateCustomerKey()
        {
            // Use explicit key for duplicate test scenario
            await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["Key"] = "duplicate-customer",
                ["DisplayName"] = "Customer 1"
            });

            await Assert.ThrowsAsync<ConflictException>(async () =>
                await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
                {
                    ["Key"] = "duplicate-customer",
                    ["DisplayName"] = "Customer 2"
                })
            );
        }

        [Fact]
        public async Task ThrowsErrorForDuplicateExternalBillingId()
        {
            await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["ExternalBillingId"] = "cus_123"
            });

            await Assert.ThrowsAsync<ConflictException>(async () =>
                await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
                {
                    ["ExternalBillingId"] = "cus_123"
                })
            );
        }

        [Fact]
        public async Task AllowsOptionalFieldsToBeUndefined()
        {
            var customer = await _fixtures.CreateCustomerAsync();

            customer.DisplayName.Should().BeNull();
            customer.Email.Should().BeNull();
            customer.ExternalBillingId.Should().BeNull();
        }
    }

    public class LifecycleStatusTests : CustomersTests
    {
        public LifecycleStatusTests() : base() { }

        [Fact]
        public async Task ActivatesASuspendedCustomer()
        {
            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Activate Customer"
            });

            await _subscrio.Customers.ArchiveCustomerAsync(customer.Key);
            var retrieved = await _subscrio.Customers.GetCustomerAsync(customer.Key);
            retrieved!.Status.Should().Be("archived");

            await _subscrio.Customers.UnarchiveCustomerAsync(customer.Key);
            retrieved = await _subscrio.Customers.GetCustomerAsync(customer.Key);
            retrieved!.Status.Should().Be("active");
        }

        [Fact]
        public async Task ArchivesAnActiveCustomer()
        {
            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Suspend Customer"
            });

            customer.Status.Should().Be("active");

            await _subscrio.Customers.ArchiveCustomerAsync(customer.Key);
            var retrieved = await _subscrio.Customers.GetCustomerAsync(customer.Key);
            retrieved!.Status.Should().Be("archived");
        }

        [Fact]
        public async Task ArchivesCustomerForDeletion()
        {
            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Mark Deleted Customer"
            });

            await _subscrio.Customers.ArchiveCustomerAsync(customer.Key);
            var retrieved = await _subscrio.Customers.GetCustomerAsync(customer.Key);
            retrieved!.Status.Should().Be("archived");
        }

        [Fact]
        public async Task DeletesCustomerOnlyWhenMarkedAsDeleted()
        {
            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Delete After Mark"
            });

            await _subscrio.Customers.ArchiveCustomerAsync(customer.Key);
            await _subscrio.Customers.DeleteCustomerAsync(customer.Key);

            var retrieved = await _subscrio.Customers.GetCustomerAsync(customer.Key);
            retrieved.Should().BeNull();
        }

        [Fact]
        public async Task ThrowsErrorWhenDeletingActiveCustomer()
        {
            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Delete Active Customer"
            });

            await Assert.ThrowsAsync<DomainException>(async () =>
                await _subscrio.Customers.DeleteCustomerAsync(customer.Key)
            );
        }

        [Fact]
        public async Task ThrowsErrorWhenDeletingActiveCustomerWithoutArchiving()
        {
            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Delete Active Customer"
            });

            // Don't archive the customer - keep it active
            await Assert.ThrowsAsync<DomainException>(async () =>
                await _subscrio.Customers.DeleteCustomerAsync(customer.Key)
            );
        }
    }

    public class ListFilterTests : CustomersTests
    {
        public ListFilterTests() : base() { }

        [Fact]
        public async Task ListsAllCustomers()
        {
            await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "List 1"
            });
            await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "List 2"
            });

            var customers = await _subscrio.Customers.ListCustomersAsync();
            customers.Should().HaveCountGreaterThanOrEqualTo(2);
        }

        [Fact]
        public async Task FiltersCustomersByStatusActive()
        {
            await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Filter Active"
            });

            var activeCustomers = await _subscrio.Customers.ListCustomersAsync(new CustomerFilterDto(
                Status: "active"
            ));
            activeCustomers.Should().AllSatisfy(c => c.Status.Should().Be("active"));
            activeCustomers.Should().NotBeEmpty();
        }

        [Fact]
        public async Task FiltersCustomersByStatusArchived()
        {
            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Filter Archived"
            });
            await _subscrio.Customers.ArchiveCustomerAsync(customer.Key);

            var archivedCustomers = await _subscrio.Customers.ListCustomersAsync(new CustomerFilterDto(
                Status: "archived"
            ));
            archivedCustomers.Should().Contain(c => c.Key == customer.Key);
        }

        [Fact]
        public async Task FiltersCustomersByStatusArchivedSecondTest()
        {
            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Filter Archived 2"
            });
            await _subscrio.Customers.ArchiveCustomerAsync(customer.Key);

            var archivedCustomers = await _subscrio.Customers.ListCustomersAsync(new CustomerFilterDto(
                Status: "archived"
            ));
            archivedCustomers.Should().Contain(c => c.Key == customer.Key);
        }

        [Fact]
        public async Task SearchesCustomersByKey()
        {
            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Search Customer"
            });

            var customers = await _subscrio.Customers.ListCustomersAsync(new CustomerFilterDto(
                Search: customer.Key
            ));
            customers.Should().Contain(c => c.Key == customer.Key);
        }

        [Fact]
        public async Task SearchesCustomersByDisplayName()
        {
            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["DisplayName"] = "Very Unique Display Name"
            });

            var customers = await _subscrio.Customers.ListCustomersAsync(new CustomerFilterDto(
                Search: "Very Unique Display"
            ));
            customers.Should().Contain(c => c.DisplayName != null && c.DisplayName.Contains("Very Unique Display Name"));
        }

        [Fact]
        public async Task SearchesCustomersByEmail()
        {
            var customer = await _fixtures.CreateCustomerAsync(new Dictionary<string, object>
            {
                ["Email"] = "unique.search@example.com"
            });

            var customers = await _subscrio.Customers.ListCustomersAsync(new CustomerFilterDto(
                Search: "unique.search@example.com"
            ));
            customers.Should().Contain(c => c.Email == "unique.search@example.com");
        }

        [Fact]
        public async Task PaginatesCustomerListLimit()
        {
            var customers = await _subscrio.Customers.ListCustomersAsync(new CustomerFilterDto(
                Limit: 5
            ));
            customers.Should().HaveCountLessThanOrEqualTo(5);
        }

        [Fact]
        public async Task PaginatesCustomerListOffset()
        {
            var allCustomers = await _subscrio.Customers.ListCustomersAsync(new CustomerFilterDto(
                Limit: 100
            ));

            if (allCustomers.Count > 5)
            {
                var firstPage = await _subscrio.Customers.ListCustomersAsync(new CustomerFilterDto(
                    Limit: 5,
                    Offset: 0
                ));
                var secondPage = await _subscrio.Customers.ListCustomersAsync(new CustomerFilterDto(
                    Limit: 5,
                    Offset: 5
                ));

                firstPage.Should().NotBeEmpty();
                if (secondPage.Count > 0)
                {
                    firstPage[0].Key.Should().NotBe(secondPage[0].Key);
                }
            }
        }
    }
}

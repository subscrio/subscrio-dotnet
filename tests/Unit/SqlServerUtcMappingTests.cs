using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Subscrio.Core.Infrastructure.Database;

namespace Subscrio.Core.Tests.Unit;

public class SqlServerUtcMappingTests
{
    [Fact]
    public void SqlServerTimestampProperties_RestoreUtcKindOnRead()
    {
        using var db = new SubscrioDbContext(new DbContextOptionsBuilder<SubscrioDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=UnusedModelOnly;Integrated Security=true")
            .Options);
        var stored = new DateTime(2030, 11, 12, 16, 30, 0, DateTimeKind.Unspecified);
        var properties = db.Model.GetEntityTypes().SelectMany(e => e.GetProperties())
            .Where(p => p.GetColumnType() == "datetime2").ToList();
        properties.Should().NotBeEmpty();
        foreach (var property in properties)
        {
            var converter = property.GetValueConverter();
            converter.Should().NotBeNull(property.Name);
            var actual = (DateTime)converter!.ConvertFromProvider(stored)!;
            actual.Kind.Should().Be(DateTimeKind.Utc, property.Name);
            actual.Ticks.Should().Be(stored.Ticks, property.Name);
            actual.ToUniversalTime().Ticks.Should().Be(stored.Ticks, property.Name);
        }
    }
}

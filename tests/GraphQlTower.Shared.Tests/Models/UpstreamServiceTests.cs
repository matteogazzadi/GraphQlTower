using FluentAssertions;
using GraphQlTower.Shared.Models;

namespace GraphQlTower.Shared.Tests.Models;

public class UpstreamServiceTests
{
    [Fact]
    public void NewUpstreamService_HasDefaultValues()
    {
        var svc = new UpstreamService();

        svc.Id.Should().NotBeEmpty();
        svc.IsEnabled.Should().BeTrue();
        svc.LastStatus.Should().Be(ServiceHealthStatus.Unknown);
        svc.Headers.Should().NotBeNull().And.BeEmpty();
        svc.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void NewServiceHeader_HasNewGuid()
    {
        var header = new ServiceHeader { Key = "Authorization", Value = "Bearer token" };

        header.Id.Should().NotBeEmpty();
        header.Key.Should().Be("Authorization");
        header.Value.Should().Be("Bearer token");
    }

    [Theory]
    [InlineData(ServiceHealthStatus.Unknown)]
    [InlineData(ServiceHealthStatus.Healthy)]
    [InlineData(ServiceHealthStatus.Degraded)]
    [InlineData(ServiceHealthStatus.Unhealthy)]
    public void ServiceHealthStatus_AllValuesAreDefined(ServiceHealthStatus status)
    {
        Enum.IsDefined(typeof(ServiceHealthStatus), status).Should().BeTrue();
    }
}

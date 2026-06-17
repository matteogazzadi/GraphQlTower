using FluentAssertions;
using GraphQlTower.Shared.DTOs;

namespace GraphQlTower.Shared.Tests.DTOs;

public class UpstreamServiceDtoTests
{
    [Fact]
    public void CreateUpstreamServiceRequest_DefaultsToEnabled()
    {
        var req = new CreateUpstreamServiceRequest();

        req.IsEnabled.Should().BeTrue();
        req.Headers.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void CreateUpstreamServiceRequest_CanSetAllProperties()
    {
        var req = new CreateUpstreamServiceRequest
        {
            Name = "products",
            DisplayName = "Products Service",
            Url = "https://api.example.com/graphql",
            IsEnabled = false,
            Headers = new List<ServiceHeaderDto>
            {
                new() { Key = "Authorization", Value = "Bearer token" }
            }
        };

        req.Name.Should().Be("products");
        req.DisplayName.Should().Be("Products Service");
        req.Url.Should().Be("https://api.example.com/graphql");
        req.IsEnabled.Should().BeFalse();
        req.Headers.Should().HaveCount(1);
        req.Headers[0].Key.Should().Be("Authorization");
    }

    [Fact]
    public void UpdateUpstreamServiceRequest_CanSetAllProperties()
    {
        var req = new UpdateUpstreamServiceRequest
        {
            DisplayName = "Updated Name",
            Url = "https://new-url.com/graphql",
            IsEnabled = true,
            Headers = new List<ServiceHeaderDto>()
        };

        req.DisplayName.Should().Be("Updated Name");
        req.Url.Should().Be("https://new-url.com/graphql");
        req.Headers.Should().BeEmpty();
    }
}

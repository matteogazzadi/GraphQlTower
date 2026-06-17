using GraphQlTower.Shared.Interfaces;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;

namespace GraphQlTower.Api.Stitching;

/// <summary>
/// Fires TypesChanged whenever the upstream service registry changes so that
/// HotChocolate evicts the current executor and rebuilds it lazily on the next
/// request.  Actual remote schema wiring is done at startup via AddRemoteSchema
/// in Program.cs; SDL is served to HC by DynamicSchemaDefinitionPublisher.
/// </summary>
public class DynamicRemoteSchemaModule : ITypeModule
{
    public event EventHandler<EventArgs>? TypesChanged;

    public DynamicRemoteSchemaModule(IServiceRegistry registry)
    {
        registry.Changes.Subscribe(_ => TypesChanged?.Invoke(this, EventArgs.Empty));
    }

    public ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
        IDescriptorContext context,
        CancellationToken cancellationToken)
        => new(Array.Empty<ITypeSystemMember>());
}

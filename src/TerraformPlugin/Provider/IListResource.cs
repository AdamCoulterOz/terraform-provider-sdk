using TerraformPlugin.Schema;
using TerraformPlugin.Types;

namespace TerraformPlugin.Provider;

internal interface IListResource
{
    ComponentSchema Schema { get; }
    ComponentSchema ResourceSchema { get; }
    IdentitySchema IdentitySchema { get; }

    ValueTask<ValidateResult> ValidateConfigAsync(ListResourceValidateRequest request, CancellationToken cancellationToken);
    IAsyncEnumerable<ListEvent> ListAsync(ListResourceRequest request, CancellationToken cancellationToken);
}

internal abstract class ListResourceBase : IListResource
{
    public abstract ComponentSchema Schema { get; }
    public abstract ComponentSchema ResourceSchema { get; }
    public abstract IdentitySchema IdentitySchema { get; }

    public virtual ValueTask<ValidateResult> ValidateConfigAsync(ListResourceValidateRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ValidateResult.Empty);

    public abstract IAsyncEnumerable<ListEvent> ListAsync(ListResourceRequest request, CancellationToken cancellationToken);
}

internal sealed record ListEvent(
    DynamicValue? Identity = null,
    string? DisplayName = null,
    DynamicValue? ResourceObject = null,
    IReadOnlyList<Diagnostics.Diagnostic>? Diagnostics = null);

# terraform-provider-sdk

An experimental .NET-native Terraform provider SDK that targets protocol v6 directly.

## Authoring Model

Provider authors work with typed classes, conventions, optional override attributes, and lifecycle methods. The tfprotov6 and gRPC plumbing stays inside the host/runtime layer.

The main authoring surface is now self-typed: the resource class is the schema source, the bound Terraform object, and the behavior implementation. Public properties define the Terraform shape. PascalCase member names become `snake_case`. Writable members become inputs, getter-only or explicitly computed members become outputs, and `[DataSourceQuery]` methods generate Terraform data sources from method parameters plus the resource's output shape.

```csharp
using TerraformPluginDotnet;
using TerraformPluginDotnet.Diagnostics;
using TerraformPluginDotnet.Hosting;
using TerraformPluginDotnet.Schema;
using TerraformPluginDotnet.Types;

return await TerraformProviderHost.RunAsync(new ExampleProvider(), args);

internal sealed class ExampleProvider : TerraformProvider<ExampleProviderConfig, ExampleProviderState>
{
    public override string TypeName => "example";

    public override IEnumerable<TerraformResource<ExampleProviderState>> Resources =>
        [new Widget()];

    public override IEnumerable<TerraformDataSource<ExampleProviderState>> DataSources => [];

    public override ValueTask<ExampleProviderState> ConfigureAsync(
        ExampleProviderConfig config,
        TerraformProviderContext context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ExampleProviderState(config.Endpoint.RequireValue()));
}

internal sealed record ExampleProviderState(string Endpoint);

internal sealed class ExampleProviderConfig
{
    [TerraformAttribute]
    public TF<string> Endpoint { get; init; }
}

internal sealed class Widget : TerraformResource<Widget, ExampleProviderState>
{
    public override string Name => "widget";

    public required TF<string> Name { get; init; }

    [TerraformAttribute(Computed = true)]
    public TF<string> Id { get; init; }

    public override ValueTask<TerraformPlanResult<Widget>> PlanAsync(
        Widget? priorState,
        TerraformResourceContext<ExampleProviderState> context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new TerraformPlanResult<Widget>(this));

    public override ValueTask<TerraformModelResult<Widget>> ApplyAsync(
        Widget? priorState,
        TerraformResourceContext<ExampleProviderState> context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new TerraformModelResult<Widget>(new Widget
        {
            Name = Name,
            Id = TF<string>.Known($"{context.ProviderState.Endpoint}/{Name.RequireValue()}"),
        }));

    public override ValueTask<TerraformModelResult<Widget>> ReadAsync(
        TerraformResourceContext<ExampleProviderState> context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new TerraformModelResult<Widget>(this));

    public override ValueTask<TerraformModelResult<Widget>> DeleteAsync(
        Widget? priorState,
        TerraformResourceContext<ExampleProviderState> context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new TerraformModelResult<Widget>(null));

    [DataSourceQuery]
    public static ValueTask<Widget?> GetAsync(
        ExampleProviderState providerState,
        TF<string> name,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<Widget?>(new Widget
        {
            Name = name,
            Id = TF<string>.Known($"{providerState.Endpoint}/{name.RequireValue()}"),
        });
}
```

`TF<T>` is the typed state wrapper for Terraform's known/null/unknown values. That keeps unknown-planned values explicit without dragging raw protobuf concepts into provider code.

## Layout

- `src/TerraformPluginDotnet`
  - Runtime SDK surface.
- `proto/`
  - Vendored protobuf definitions used to generate the gRPC server bindings.
- `samples/File`
  - A small working provider that manages files on disk.
- `samples/Azure`
  - A working provider that manages Azure Blob Storage content through `Azure.Storage.Blobs`.
  - The root Terraform provider type is `az`, while Azure Resource Provider nodes such as `Storage : AzureProvider("Microsoft.Storage")` contribute composed child names like `az_storage_blob`.
- `tests/TerraformPluginDotnet.E2E`
  - Publishes the sample providers and runs them through the real Terraform CLI.

## Design Goals

- Target `tfprotov6` directly.
- Present a typed, declarative, idiomatic .NET authoring surface.
- Keep protocol-shaped request/response and protobuf concerns internal to the runtime.
- Avoid recreating `terraform-plugin-sdk/v2` compatibility behavior.
- Let Terraform Core remain responsible for graph building and convergence planning.
- Fail explicitly for unsupported optional protocol features.

## Current Scope

Implemented:

- typed provider/resource authoring surface with self-typed resource classes
- query-generated data sources via `[DataSourceQuery]`
- declarative schema modeling with attributes
- diagnostics and attribute paths
- Terraform type/value modeling
- dynamic value msgpack and JSON conversion
- internal tfprotov6 adapter layer
- go-plugin bootstrap services
- automatic mTLS handshake support required by Terraform CLI
- same-type resource import support
- private state passthrough on typed resources
- sample providers verified with real Terraform CLI schema/apply/import/plan/destroy

Not implemented yet:

- legacy flatmap state upgrade
- functions
- actions
- ephemeral resources
- list resources
- state stores
- resource identity behavior beyond placeholder/no-op support
- heterogeneous multi-resource-type import results

## Running the End-to-End Check

From the repository root:

```bash
dotnet run --project tests/TerraformPluginDotnet.E2E/TerraformPluginDotnet.E2E.csproj
```

That command publishes `samples/File`, runs Terraform with a development override, applies configuration, verifies convergence, and destroys the created resource.

The end-to-end harness also publishes `samples/Azure`, starts a local Azurite blob emulator, and exercises the `example/az` provider with the `az_storage_blob` resource/data source through the real Terraform CLI.

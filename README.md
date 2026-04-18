# terraform-provider-sdk

An experimental .NET-native Terraform provider SDK that targets protocol v6 directly.

## Authoring Model

Provider authors work with typed classes, conventions, optional override attributes, and lifecycle methods. The tfprotov6 and gRPC plumbing stays inside the host/runtime layer.

Public settable properties participate in schema inference by default. Member names are derived from PascalCase to `snake_case`. Use `TerraformAttribute` or `TerraformNestedBlock` only when you need to override the default shape or add metadata such as `Computed`, `Optional`, descriptions, or explicit nesting rules.

```csharp
using TerraformPluginDotnet;
using TerraformPluginDotnet.Diagnostics;
using TerraformPluginDotnet.Hosting;
using TerraformPluginDotnet.Schema;
using TerraformPluginDotnet.Types;

return await TerraformProviderHost.RunAsync(new ExampleProvider(), args);

internal sealed class ExampleProvider : TerraformProvider<ExampleProviderConfig, ExampleProviderState>
{
    public override IEnumerable<TerraformResource<ExampleProviderState>> Resources =>
        [new WidgetResource()];

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

internal sealed class WidgetResource : TerraformResource<WidgetModel, ExampleProviderState>
{
    public override string TypeName => "example_widget";

    public override ValueTask<TerraformPlanResult<WidgetModel>> PlanAsync(
        WidgetModel? priorState,
        WidgetModel? proposedState,
        TerraformResourceContext<ExampleProviderState> context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new TerraformPlanResult<WidgetModel>(proposedState));

    public override ValueTask<TerraformModelResult<WidgetModel>> ApplyAsync(
        WidgetModel? priorState,
        WidgetModel? plannedState,
        TerraformResourceContext<ExampleProviderState> context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new TerraformModelResult<WidgetModel>(plannedState));

    public override ValueTask<TerraformModelResult<WidgetModel>> ReadAsync(
        WidgetModel? currentState,
        TerraformResourceContext<ExampleProviderState> context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new TerraformModelResult<WidgetModel>(currentState));
}

internal sealed class WidgetModel
{
    [TerraformAttribute]
    public TF<string> Name { get; init; }

    [TerraformAttribute(Computed = true)]
    public TF<string> Id { get; init; }
}
```

`TF<T>` is the typed state wrapper for Terraform's known/null/unknown values. That keeps unknown-planned values explicit without dragging raw protobuf concepts into provider code.

## Layout

- `src/TerraformPluginDotnet`
  - Runtime SDK surface.
- `proto/`
  - Vendored protobuf definitions used to generate the gRPC server bindings.
- `samples/TerraformProviderFile`
  - A small working provider that manages files on disk.
- `tests/TerraformPluginDotnet.E2E`
  - Publishes the sample provider and runs it through the real Terraform CLI.

## Design Goals

- Target `tfprotov6` directly.
- Present a typed, declarative, idiomatic .NET authoring surface.
- Keep protocol-shaped request/response and protobuf concerns internal to the runtime.
- Avoid recreating `terraform-plugin-sdk/v2` compatibility behavior.
- Let Terraform Core remain responsible for graph building and convergence planning.
- Fail explicitly for unsupported optional protocol features.

## Current Scope

Implemented:

- typed provider/resource/data source authoring surface
- declarative schema modeling with attributes
- diagnostics and attribute paths
- Terraform type/value modeling
- dynamic value msgpack and JSON conversion
- internal tfprotov6 adapter layer
- go-plugin bootstrap services
- automatic mTLS handshake support required by Terraform CLI
- sample provider verified with real Terraform CLI apply/plan/destroy

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

That command publishes `samples/TerraformProviderFile`, runs Terraform with a development override, applies configuration, verifies convergence, and destroys the created resource.

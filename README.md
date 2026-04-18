# terraform-provider-sdk

An experimental .NET-native Terraform provider SDK that targets protocol v6 directly.

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
- Keep the provider-facing surface protocol-shaped and .NET-idiomatic.
- Avoid recreating `terraform-plugin-sdk/v2` compatibility behavior.
- Let Terraform Core remain responsible for graph building and convergence planning.
- Fail explicitly for unsupported optional protocol features.

## Current Scope

Implemented:

- provider/resource/data source contracts
- schema modeling
- diagnostics and attribute paths
- Terraform type/value modeling
- dynamic value msgpack and JSON conversion
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

## Running the End-to-End Check

From the repository root:

```bash
dotnet run --project tests/TerraformPluginDotnet.E2E/TerraformPluginDotnet.E2E.csproj
```

That command publishes `samples/TerraformProviderFile`, runs Terraform with a development override, applies configuration, verifies convergence, and destroys the created resource.

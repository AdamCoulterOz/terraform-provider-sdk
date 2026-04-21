# Project Context

## Purpose and Current State

This repository contains an experimental .NET-native Terraform provider SDK that targets Terraform protocol v6 directly.

Current state:

- `src/TerraformPluginDotnet` provides a focused tfprotov6 runtime for .NET providers:
  - typed provider and self-typed resource base classes
  - query-generated data sources via attributed static methods on resource classes
  - declarative schema inference from resource/model classes using convention-first public properties plus optional override attributes
  - diagnostics and attribute paths
  - Terraform type/value modeling
  - dynamic value msgpack and JSON translation
  - go-plugin-compatible gRPC hosting, including automatic mTLS handshake support
  - internal adapters that translate the typed public surface into tfprotov6 RPC handlers
- `samples/File` is a working sample provider.
- `samples/Azure` is a working Azure Blob Storage sample provider built on `Azure.Storage.Blobs`, exposed to Terraform as provider type `az` with resource/data source type `az_storage_blob`.
- `tests/TerraformPluginDotnet.E2E` publishes the sample providers and exercises them with the real Terraform CLI, including schema loading, apply, import, converged plan, and destroy.

## Architecture and Structure

- `proto/`
  - Vendored protocol definitions copied from upstream releases:
    - `tfplugin6/tfplugin6.proto`
    - go-plugin broker/controller/stdio protos
- `src/TerraformPluginDotnet`
  - Focused .NET runtime layer.
  - Public root namespace:
    - `TerraformProvider<TConfig, TProviderState>`
    - `TerraformResource<TSelf, TProviderState>`
    - `TerraformDataSource<TModel, TProviderState>`
    - `DataSourceQueryAttribute`
    - typed result/context records
  - `Hosting/`
    - plugin bootstrap
    - go-plugin handshake
    - automatic mTLS support
    - gRPC provider server implementation
  - `Provider/`
    - internal protocol adapter layer
    - internal provider/resource/data source contracts
    - internal request/response models
  - `Schema/`
    - schema block/attribute/nested-block model plus proto mapping
    - declarative attribute-based schema inference
  - `Types/`
    - Terraform type system model
    - known/null/unknown value representation
    - generic `TF<T>` wrapper for provider models
    - msgpack and JSON dynamic value translation
  - `Diagnostics/`
    - diagnostics severity and attribute-path aware diagnostics
- `samples/File`
  - Small file-based provider used to prove runtime behavior against Terraform CLI.
- `samples/Azure`
  - Azure Blob Storage provider using the real .NET Azure client SDK.
  - The root Terraform provider is `Provider` with type `az`.
  - Azure Resource Provider nodes are modeled separately, for example `Storage : AzureProvider("Microsoft.Storage")`, which contributes the `storage` segment used by child resources and data sources such as `az_storage_blob`.
- `tests/TerraformPluginDotnet.E2E`
  - End-to-end CLI harness for publish/schema/apply/import/plan/destroy verification.

## Key Decisions and Invariants

- The SDK intentionally targets `tfprotov6` directly.
- The public authoring surface is intentionally .NET-first, with self-typed resource classes and declarative attributes.
- Public resource members participate in schema inference by convention:
  - PascalCase member names become `snake_case`
  - writable scalar and scalar-collection members become inputs/state attributes
  - getter-only or explicitly computed members become computed outputs
  - complex-type members and collections of complex types become nested blocks
  - attributes remain available as overrides for naming, computed/optional flags, descriptions, and explicit nesting behavior
- Static methods marked with `DataSourceQueryAttribute` generate Terraform data sources:
  - method parameters define query inputs
  - a singular result projects the resource shape as the data source state
  - collection results project as a computed `items` list of the resource object shape
- The tfprotov6/protobuf request-response layer is an internal implementation detail, not the provider authoring API.
- It does not try to emulate `terraform-plugin-sdk/v2`, `helper/schema`, or Terraform Core planning internals.
- Planning remains Terraform Core's responsibility. Provider-side planning is limited to:
  - proposed-state shaping
  - computed value finalization where needed
  - replace-path signaling
  - provider-specific validation and apply/read behavior
- go-plugin interoperability requires more than raw gRPC:
  - magic-cookie launch validation
  - broker/controller/stdio service endpoints
  - health service registration
  - automatic mTLS handshake support using `PLUGIN_CLIENT_CERT`
  - the runtime now binds Kestrel directly to loopback on port `0` and emits the actual bound endpoint after startup, avoiding the previous reserve-then-bind race
  - generated server certificates include both `localhost` and `127.0.0.1` SANs for the advertised loopback endpoint
- State upgrade support is intentionally narrow:
  - current-schema JSON state upgrade is supported
  - legacy flatmap upgrade is still unsupported
- Unsupported tfprotov6 feature families fail explicitly rather than silently degrading:
  - functions
  - actions
  - ephemeral resources
  - list resources
  - state stores
  - resource identity support beyond empty/no-op responses
- Resource import support is currently same-type only:
  - a resource may return zero or more imported instances of its own Terraform type
  - heterogeneous multi-resource-type import results are not yet supported by the typed authoring abstraction
- Typed adapters now treat unexpected provider exceptions as structured Terraform diagnostics instead of crashing the plugin process.
- Typed resource contexts now carry Terraform private state through read, plan, and apply operations.
- Provider session state storage is synchronized for concurrent RPC reads and writes.
- Declarative schema inference rejects recursive object-attribute models explicitly instead of recursing until stack overflow.
- Declarative schema inference now ignores inherited SDK infrastructure members and only reflects the user-declared Terraform surface on the concrete resource/model type.

## Outstanding Actions

- Add automated tests around:
  - dynamic value round-tripping
  - model binding between typed provider models and Terraform values
  - provider schema serialization
  - state upgrade edge cases
  - mTLS/bootstrap behavior without needing full Terraform CLI runs
  - typed private-state round-tripping
  - schema recursion failures
- Decide how much more tfprotov6 coverage belongs in the initial SDK:
  - resource identity
  - deferred responses
  - provider meta
  - provider functions
- Define the public versioning and package strategy if this becomes a real distributable artifact.

## Technical Debt and Follow-Up Notes

- `src/TerraformPluginDotnet/Hosting/ProviderRpcService.cs` still contains a fair amount of protocol adaptation logic in one class.
- `src/TerraformPluginDotnet/Provider/TerraformModelBinder.cs` is now a key bridge between the typed public model layer and the internal protocol representation, and needs deeper coverage for collections/object edge cases.
- `src/TerraformPluginDotnet/Provider/ReflectedQueryDataSource.cs` is a first pass at method-backed data source generation and should be hardened with broader query-shape coverage.
- `src/TerraformPluginDotnet/Types/TerraformDynamicValueSerializer.cs` is the most correctness-sensitive part of the SDK and should get dedicated tests before wider use.
- The current end-to-end test uses Terraform CLI dev overrides, which is appropriate for development but not a packaging or release story.
- The Azure sample provider and Azurite-backed E2E prove the CLI handshake and lifecycle path with a real third-party SDK, but they are still development-time examples rather than a packaging story.
- The runtime still does not expose `provider_meta`, heterogeneous import results, or old-SDK-style semantic shims, by design.

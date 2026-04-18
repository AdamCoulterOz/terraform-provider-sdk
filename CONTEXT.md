# Project Context

## Purpose and Current State

This repository contains an experimental .NET-native Terraform provider SDK that targets Terraform protocol v6 directly.

Current state:

- `src/TerraformPluginDotnet` provides a focused tfprotov6 runtime for .NET providers:
  - typed provider, resource, and data source base classes
  - declarative schema inference from model classes using convention-first public properties plus optional override attributes
  - diagnostics and attribute paths
  - Terraform type/value modeling
  - dynamic value msgpack and JSON translation
  - go-plugin-compatible gRPC hosting, including automatic mTLS handshake support
  - internal adapters that translate the typed public surface into tfprotov6 RPC handlers
- `samples/TerraformProviderFile` is a working sample provider.
- `tests/TerraformPluginDotnet.E2E` publishes the sample provider and exercises it with the real Terraform CLI.

## Architecture and Structure

- `proto/`
  - Vendored protocol definitions copied from upstream releases:
    - `tfplugin6/tfplugin6.proto`
    - go-plugin broker/controller/stdio protos
- `src/TerraformPluginDotnet`
  - Focused .NET runtime layer.
  - Public root namespace:
    - `TerraformProvider<TConfig, TProviderState>`
    - `TerraformResource<TModel, TProviderState>`
    - `TerraformDataSource<TModel, TProviderState>`
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
- `samples/TerraformProviderFile`
  - Small file-based provider used to prove runtime behavior against Terraform CLI.
- `tests/TerraformPluginDotnet.E2E`
  - End-to-end CLI harness for publish/apply/plan/destroy verification.

## Key Decisions and Invariants

- The SDK intentionally targets `tfprotov6` directly.
- The public authoring surface is intentionally .NET-first, with typed model classes and declarative attributes.
- Public settable members participate in schema inference by convention:
  - PascalCase member names become `snake_case`
  - scalar and scalar-collection members become attributes
  - complex-type members and collections of complex types become nested blocks
  - attributes remain available as overrides for naming, computed/optional flags, descriptions, and explicit nesting behavior
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

## Outstanding Actions

- Add automated tests around:
  - dynamic value round-tripping
  - model binding between typed provider models and Terraform values
  - provider schema serialization
  - state upgrade edge cases
  - mTLS/bootstrap behavior without needing full Terraform CLI runs
- Decide how much more tfprotov6 coverage belongs in the initial SDK:
  - resource identity
  - deferred responses
  - provider meta
  - provider functions
- Define the public versioning and package strategy if this becomes a real distributable artifact.

## Technical Debt and Follow-Up Notes

- `src/TerraformPluginDotnet/Hosting/ProviderRpcService.cs` still contains a fair amount of protocol adaptation logic in one class.
- `src/TerraformPluginDotnet/Provider/TerraformModelBinder.cs` is now a key bridge between the typed public model layer and the internal protocol representation, and needs deeper coverage for collections/object edge cases.
- `src/TerraformPluginDotnet/Types/TerraformDynamicValueSerializer.cs` is the most correctness-sensitive part of the SDK and should get dedicated tests before wider use.
- The current end-to-end test uses Terraform CLI dev overrides, which is appropriate for development but not a packaging or release story.
- The sample provider proves the core lifecycle path, but it is intentionally small and does not cover the optional tfprotov6 feature surface.

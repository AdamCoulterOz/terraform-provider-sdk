# Project Context

## Purpose and Current State

This repository contains an experimental .NET-native Terraform provider SDK that targets Terraform protocol v6 directly.

Current state:

- `src/TerraformPluginDotnet` provides a focused tfprotov6 surface for .NET providers:
  - provider, resource, and data source interfaces
  - schema modeling
  - diagnostics and attribute paths
  - Terraform type/value modeling
  - dynamic value msgpack and JSON translation
  - go-plugin-compatible gRPC hosting, including automatic mTLS handshake support
- `samples/TerraformProviderFile` is a working sample provider.
- `tests/TerraformPluginDotnet.E2E` publishes the sample provider and exercises it with the real Terraform CLI.

## Architecture and Structure

- `proto/`
  - Vendored protocol definitions copied from upstream releases:
    - `tfplugin6/tfplugin6.proto`
    - go-plugin broker/controller/stdio protos
- `src/TerraformPluginDotnet`
  - Focused .NET runtime layer.
  - `Hosting/`
    - plugin bootstrap
    - go-plugin handshake
    - automatic mTLS support
    - gRPC provider server implementation
  - `Provider/`
    - protocol-shaped provider/resource/data source contracts
    - request/response models
  - `Schema/`
    - schema block/attribute/nested-block model plus proto mapping
  - `Types/`
    - Terraform type system model
    - known/null/unknown value representation
    - msgpack and JSON dynamic value translation
  - `Diagnostics/`
    - diagnostics severity and attribute-path aware diagnostics
- `samples/TerraformProviderFile`
  - Small file-based provider used to prove runtime behavior against Terraform CLI.
- `tests/TerraformPluginDotnet.E2E`
  - End-to-end CLI harness for publish/apply/plan/destroy verification.

## Key Decisions and Invariants

- The SDK intentionally targets `tfprotov6` directly.
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

## Outstanding Actions

- Add automated tests around:
  - dynamic value round-tripping
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
- `src/TerraformPluginDotnet/Types/TerraformDynamicValueSerializer.cs` is the most correctness-sensitive part of the SDK and should get dedicated tests before wider use.
- The current end-to-end test uses Terraform CLI dev overrides, which is appropriate for development but not a packaging or release story.
- The sample provider proves the core lifecycle path, but it is intentionally small and does not cover the optional tfprotov6 feature surface.

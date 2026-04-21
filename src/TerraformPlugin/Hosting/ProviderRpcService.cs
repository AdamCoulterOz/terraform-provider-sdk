using Grpc.Core;
using Microsoft.Extensions.Hosting;
using TerraformPlugin.Diagnostics;
using TerraformPlugin.Provider;
using TerraformPlugin.Schema;
using TerraformPlugin.Types;
using Tfplugin6;
using ProviderServiceBase = Tfplugin6.Provider.ProviderBase;
using ProtocolDynamicValue = Tfplugin6.DynamicValue;

namespace TerraformPlugin.Hosting;

internal sealed class ProviderRpcService(
    IProvider provider,
    ProviderSessionState sessionState,
    IHostApplicationLifetime applicationLifetime) : ProviderServiceBase
{
    public override Task<GetMetadata.Types.Response> GetMetadata(GetMetadata.Types.Request request, ServerCallContext context)
    {
        var response = new GetMetadata.Types.Response
        {
            ServerCapabilities = new ServerCapabilities
            {
                GetProviderSchemaOptional = true,
                MoveResourceState = false,
                PlanDestroy = false,
                GenerateResourceConfig = false,
            },
        };

        response.Resources.AddRange(provider.Resources.Keys.OrderBy(static key => key, StringComparer.Ordinal).Select(
            static key => new GetMetadata.Types.ResourceMetadata { TypeName = key }));

        response.DataSources.AddRange(provider.DataSources.Keys.OrderBy(static key => key, StringComparer.Ordinal).Select(
            static key => new GetMetadata.Types.DataSourceMetadata { TypeName = key }));

        response.ListResources.AddRange(provider.ListResources.Keys.OrderBy(static key => key, StringComparer.Ordinal).Select(
            static key => new GetMetadata.Types.ListResourceMetadata { TypeName = key }));

        return Task.FromResult(response);
    }

    public override Task<GetProviderSchema.Types.Response> GetProviderSchema(GetProviderSchema.Types.Request request, ServerCallContext context)
    {
        var response = new GetProviderSchema.Types.Response
        {
            ServerCapabilities = new ServerCapabilities
            {
                GetProviderSchemaOptional = true,
                MoveResourceState = false,
                PlanDestroy = false,
                GenerateResourceConfig = false,
            },
            Provider = SchemaMapper.ToProtocolSchema(provider.ProviderSchema),
        };

        if (provider.ProviderMetaSchema is not null)
        {
            response.ProviderMeta = SchemaMapper.ToProtocolSchema(provider.ProviderMetaSchema);
        }

        foreach (var resource in provider.Resources.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            response.ResourceSchemas[resource.Key] = SchemaMapper.ToProtocolSchema(resource.Value.Schema);
        }

        foreach (var dataSource in provider.DataSources.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            response.DataSourceSchemas[dataSource.Key] = SchemaMapper.ToProtocolSchema(dataSource.Value.Schema);
        }

        foreach (var listResource in provider.ListResources.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            response.ListResourceSchemas[listResource.Key] = SchemaMapper.ToProtocolSchema(listResource.Value.Schema);
        }

        return Task.FromResult(response);
    }

    public override Task<GetResourceIdentitySchemas.Types.Response> GetResourceIdentitySchemas(GetResourceIdentitySchemas.Types.Request request, ServerCallContext context)
    {
        var response = new GetResourceIdentitySchemas.Types.Response();

        foreach (var resource in provider.Resources.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (resource.Value.IdentitySchema is null)
            {
                continue;
            }

            response.IdentitySchemas[resource.Key] = SchemaMapper.ToProtocolIdentitySchema(resource.Value.IdentitySchema);
        }

        return Task.FromResult(response);
    }

    public override async Task<ValidateProviderConfig.Types.Response> ValidateProviderConfig(ValidateProviderConfig.Types.Request request, ServerCallContext context)
    {
        var config = DynamicValueSerializer.DecodeDynamicValue(request.Config, provider.ProviderSchema.Block.ValueType());
        var result = await provider.ValidateConfigAsync(new ProviderValidateRequest(config), context.CancellationToken).ConfigureAwait(false);
        return new ValidateProviderConfig.Types.Response { Diagnostics = { result.Diagnostics?.Select(ToProtocolDiagnostic) ?? [] } };
    }

    public override async Task<ConfigureProvider.Types.Response> ConfigureProvider(ConfigureProvider.Types.Request request, ServerCallContext context)
    {
        var config = DynamicValueSerializer.DecodeDynamicValue(request.Config, provider.ProviderSchema.Block.ValueType());
        var result = await provider.ConfigureAsync(
            new ProviderConfigureRequest(
                config,
                request.TerraformVersion,
                request.ClientCapabilities?.DeferralAllowed ?? false),
            context.CancellationToken).ConfigureAwait(false);

        sessionState.ProviderState = result.ProviderState;

        return new ConfigureProvider.Types.Response
        {
            Diagnostics = { result.Diagnostics?.Select(ToProtocolDiagnostic) ?? [] },
        };
    }

    public override Task<UpgradeResourceState.Types.Response> UpgradeResourceState(UpgradeResourceState.Types.Request request, ServerCallContext context)
    {
        var resource = GetResource(request.TypeName);
        var stateType = resource.Schema.Block.ValueType();
        var response = new UpgradeResourceState.Types.Response();

        if (request.RawState.Json.Length > 0)
        {
            var upgradedState = DynamicValueSerializer.DecodeJsonValue(request.RawState.Json.Span, stateType);
            response.UpgradedState = DynamicValueSerializer.EncodeDynamicValue(upgradedState, stateType);
            return Task.FromResult(response);
        }

        if (request.RawState.Flatmap.Count == 0)
        {
            response.UpgradedState = DynamicValueSerializer.EncodeDynamicValue(Types.DynamicValue.Null(stateType), stateType);
            return Task.FromResult(response);
        }

        response.Diagnostics.Add(ToProtocolDiagnostic(Diagnostics.Diagnostic.Error(
            "State Upgrade Not Supported",
            "This focused .NET SDK does not implement legacy flatmap state upgrades.")));

        return Task.FromResult(response);
    }

    public override Task<UpgradeResourceIdentity.Types.Response> UpgradeResourceIdentity(UpgradeResourceIdentity.Types.Request request, ServerCallContext context)
    {
        var response = new UpgradeResourceIdentity.Types.Response();

        response.Diagnostics.Add(ToProtocolDiagnostic(Diagnostics.Diagnostic.Error(
            "Resource Identity Not Supported",
            "This focused .NET SDK does not yet implement resource identity upgrades.")));

        return Task.FromResult(response);
    }

    public override async Task<ValidateResourceConfig.Types.Response> ValidateResourceConfig(ValidateResourceConfig.Types.Request request, ServerCallContext context)
    {
        var resource = GetResource(request.TypeName);
        var config = DynamicValueSerializer.DecodeDynamicValue(request.Config, resource.Schema.Block.ValueType());
        var result = await resource.ValidateConfigAsync(new ValidateRequest(config), context.CancellationToken).ConfigureAwait(false);
        return new ValidateResourceConfig.Types.Response { Diagnostics = { result.Diagnostics?.Select(ToProtocolDiagnostic) ?? [] } };
    }

    public override async Task<ValidateDataResourceConfig.Types.Response> ValidateDataResourceConfig(ValidateDataResourceConfig.Types.Request request, ServerCallContext context)
    {
        var dataSource = GetDataSource(request.TypeName);
        var config = DynamicValueSerializer.DecodeDynamicValue(request.Config, dataSource.Schema.Block.ValueType());
        var result = await dataSource.ValidateConfigAsync(new DataSourceValidateRequest(config), context.CancellationToken).ConfigureAwait(false);
        return new ValidateDataResourceConfig.Types.Response { Diagnostics = { result.Diagnostics?.Select(ToProtocolDiagnostic) ?? [] } };
    }

    public override async Task<ReadResource.Types.Response> ReadResource(ReadResource.Types.Request request, ServerCallContext context)
    {
        var resource = GetResource(request.TypeName);
        var stateType = resource.Schema.Block.ValueType();
        var currentState = DynamicValueSerializer.DecodeDynamicValue(request.CurrentState, stateType);
        var result = await resource.ReadAsync(
            new ResourceReadRequest(currentState, request.Private.ToByteArray(), sessionState.ProviderState),
            context.CancellationToken).ConfigureAwait(false);

        return new ReadResource.Types.Response
        {
            NewState = DynamicValueSerializer.EncodeDynamicValue(result.NewState, stateType),
            Private = Google.Protobuf.ByteString.CopyFrom(result.PrivateState ?? []),
            Diagnostics = { result.Diagnostics?.Select(ToProtocolDiagnostic) ?? [] },
            NewIdentity = ToProtocolIdentity(resource, result.NewState),
        };
    }

    public override async Task<PlanResourceChange.Types.Response> PlanResourceChange(PlanResourceChange.Types.Request request, ServerCallContext context)
    {
        var resource = GetResource(request.TypeName);
        var stateType = resource.Schema.Block.ValueType();
        var priorState = DynamicValueSerializer.DecodeDynamicValue(request.PriorState, stateType);
        var proposedNewState = DynamicValueSerializer.DecodeDynamicValue(request.ProposedNewState, stateType);
        var config = DynamicValueSerializer.DecodeDynamicValue(request.Config, stateType);
        var result = await resource.PlanAsync(
            new ResourcePlanRequest(priorState, proposedNewState, config, request.PriorPrivate.ToByteArray(), sessionState.ProviderState),
            context.CancellationToken).ConfigureAwait(false);

        var response = new PlanResourceChange.Types.Response
        {
            PlannedState = DynamicValueSerializer.EncodeDynamicValue(result.PlannedState, stateType),
            PlannedPrivate = Google.Protobuf.ByteString.CopyFrom(result.PlannedPrivateState ?? []),
            Diagnostics = { result.Diagnostics?.Select(ToProtocolDiagnostic) ?? [] },
            PlannedIdentity = ToProtocolIdentity(resource, result.PlannedState),
        };

        response.RequiresReplace.AddRange(result.RequiresReplace?.Select(ToProtocolAttributePath) ?? []);
        return response;
    }

    public override async Task<ApplyResourceChange.Types.Response> ApplyResourceChange(ApplyResourceChange.Types.Request request, ServerCallContext context)
    {
        var resource = GetResource(request.TypeName);
        var stateType = resource.Schema.Block.ValueType();
        var priorState = DynamicValueSerializer.DecodeDynamicValue(request.PriorState, stateType);
        var plannedState = DynamicValueSerializer.DecodeDynamicValue(request.PlannedState, stateType);
        var config = DynamicValueSerializer.DecodeDynamicValue(request.Config, stateType);
        var result = await resource.ApplyAsync(
            new ResourceApplyRequest(priorState, plannedState, config, request.PlannedPrivate.ToByteArray(), sessionState.ProviderState),
            context.CancellationToken).ConfigureAwait(false);

        return new ApplyResourceChange.Types.Response
        {
            NewState = DynamicValueSerializer.EncodeDynamicValue(result.NewState, stateType),
            Private = Google.Protobuf.ByteString.CopyFrom(result.PrivateState ?? []),
            Diagnostics = { result.Diagnostics?.Select(ToProtocolDiagnostic) ?? [] },
            NewIdentity = ToProtocolIdentity(resource, result.NewState),
        };
    }

    public override async Task<ImportResourceState.Types.Response> ImportResourceState(ImportResourceState.Types.Request request, ServerCallContext context)
    {
        var resource = GetResource(request.TypeName);
        var importId = ResolveImportId(resource, request);
        var result = await resource.ImportAsync(
            new ResourceImportRequest(request.TypeName, importId, sessionState.ProviderState),
            context.CancellationToken).ConfigureAwait(false);

        var response = new ImportResourceState.Types.Response
        {
            Diagnostics = { result.Diagnostics?.Select(ToProtocolDiagnostic) ?? [] },
        };

        foreach (var imported in result.Resources)
        {
            var importedResource = new ImportResourceState.Types.ImportedResource
            {
                TypeName = request.TypeName,
                State = DynamicValueSerializer.EncodeDynamicValue(imported.State, resource.Schema.Block.ValueType()),
                Private = Google.Protobuf.ByteString.CopyFrom(imported.PrivateState ?? []),
                Identity = ToProtocolIdentity(resource, imported.State),
            };

            response.ImportedResources.Add(importedResource);
        }

        return response;
    }

    public override Task<MoveResourceState.Types.Response> MoveResourceState(MoveResourceState.Types.Request request, ServerCallContext context)
    {
        var response = new MoveResourceState.Types.Response();
        response.Diagnostics.Add(ToProtocolDiagnostic(Diagnostics.Diagnostic.Error(
            "Move Resource State Not Supported",
            "This focused .NET SDK does not implement MoveResourceState.")));
        return Task.FromResult(response);
    }

    public override async Task<ReadDataSource.Types.Response> ReadDataSource(ReadDataSource.Types.Request request, ServerCallContext context)
    {
        var dataSource = GetDataSource(request.TypeName);
        var stateType = dataSource.Schema.Block.ValueType();
        var config = DynamicValueSerializer.DecodeDynamicValue(request.Config, stateType);
        var result = await dataSource.ReadAsync(new DataSourceReadRequest(config, sessionState.ProviderState), context.CancellationToken).ConfigureAwait(false);

        return new ReadDataSource.Types.Response
        {
            State = DynamicValueSerializer.EncodeDynamicValue(result.NewState, stateType),
            Diagnostics = { result.Diagnostics?.Select(ToProtocolDiagnostic) ?? [] },
        };
    }

    public override Task<GenerateResourceConfig.Types.Response> GenerateResourceConfig(GenerateResourceConfig.Types.Request request, ServerCallContext context)
    {
        var response = new GenerateResourceConfig.Types.Response();
        response.Diagnostics.Add(ToProtocolDiagnostic(Diagnostics.Diagnostic.Error(
            "Generate Config Not Supported",
            "This focused .NET SDK does not implement GenerateResourceConfig.")));
        return Task.FromResult(response);
    }

    public override Task<ValidateEphemeralResourceConfig.Types.Response> ValidateEphemeralResourceConfig(ValidateEphemeralResourceConfig.Types.Request request, ServerCallContext context) =>
        Task.FromResult(new ValidateEphemeralResourceConfig.Types.Response());

    public override Task<OpenEphemeralResource.Types.Response> OpenEphemeralResource(OpenEphemeralResource.Types.Request request, ServerCallContext context) =>
        Task.FromException<OpenEphemeralResource.Types.Response>(new RpcException(new Status(StatusCode.Unimplemented, "Ephemeral resources are not supported.")));

    public override Task<RenewEphemeralResource.Types.Response> RenewEphemeralResource(RenewEphemeralResource.Types.Request request, ServerCallContext context) =>
        Task.FromException<RenewEphemeralResource.Types.Response>(new RpcException(new Status(StatusCode.Unimplemented, "Ephemeral resources are not supported.")));

    public override Task<CloseEphemeralResource.Types.Response> CloseEphemeralResource(CloseEphemeralResource.Types.Request request, ServerCallContext context) =>
        Task.FromResult(new CloseEphemeralResource.Types.Response());

    public override async Task<ValidateListResourceConfig.Types.Response> ValidateListResourceConfig(ValidateListResourceConfig.Types.Request request, ServerCallContext context)
    {
        var listResource = GetListResource(request.TypeName);
        var config = DynamicValueSerializer.DecodeDynamicValue(request.Config, listResource.Schema.Block.ValueType());
        var includeResourceObject = DecodeOptionalDynamicValue(request.IncludeResourceObject, TFType.Bool);
        var limit = DecodeOptionalDynamicValue(request.Limit, TFType.Number);
        var result = await listResource.ValidateConfigAsync(
            new ListResourceValidateRequest(config, includeResourceObject, limit),
            context.CancellationToken).ConfigureAwait(false);

        return new ValidateListResourceConfig.Types.Response
        {
            Diagnostics = { result.Diagnostics?.Select(ToProtocolDiagnostic) ?? [] },
        };
    }

    public override async Task ListResource(ListResource.Types.Request request, IServerStreamWriter<ListResource.Types.Event> responseStream, ServerCallContext context)
    {
        var listResource = GetListResource(request.TypeName);
        var config = DynamicValueSerializer.DecodeDynamicValue(request.Config, listResource.Schema.Block.ValueType());

        await foreach (var item in listResource.ListAsync(
                           new ListResourceRequest(
                               config,
                               request.IncludeResourceObject,
                               request.Limit,
                               sessionState.ProviderState),
                           context.CancellationToken).ConfigureAwait(false))
        {
            var protocolEvent = new ListResource.Types.Event
            {
                DisplayName = item.DisplayName ?? string.Empty,
            };

            if (item.Identity is not null)
            {
                protocolEvent.Identity = new ResourceIdentityData
                {
                    IdentityData = DynamicValueSerializer.EncodeDynamicValue(item.Identity, listResource.IdentitySchema.ValueType()),
                };
            }

            if (item.ResourceObject is not null)
            {
                protocolEvent.ResourceObject = DynamicValueSerializer.EncodeDynamicValue(item.ResourceObject, listResource.ResourceSchema.Block.ValueType());
            }

            protocolEvent.Diagnostic.AddRange(item.Diagnostics?.Select(ToProtocolDiagnostic) ?? []);
            await responseStream.WriteAsync(protocolEvent).ConfigureAwait(false);
        }
    }

    public override Task<GetFunctions.Types.Response> GetFunctions(GetFunctions.Types.Request request, ServerCallContext context) =>
        Task.FromResult(new GetFunctions.Types.Response());

    public override Task<CallFunction.Types.Response> CallFunction(CallFunction.Types.Request request, ServerCallContext context) =>
        Task.FromException<CallFunction.Types.Response>(new RpcException(new Status(StatusCode.Unimplemented, "Functions are not supported.")));

    public override Task<ValidateActionConfig.Types.Response> ValidateActionConfig(ValidateActionConfig.Types.Request request, ServerCallContext context) =>
        Task.FromResult(new ValidateActionConfig.Types.Response());

    public override Task<PlanAction.Types.Response> PlanAction(PlanAction.Types.Request request, ServerCallContext context) =>
        Task.FromException<PlanAction.Types.Response>(new RpcException(new Status(StatusCode.Unimplemented, "Actions are not supported.")));

    public override Task InvokeAction(InvokeAction.Types.Request request, IServerStreamWriter<InvokeAction.Types.Event> responseStream, ServerCallContext context) =>
        Task.FromException(new RpcException(new Status(StatusCode.Unimplemented, "Actions are not supported.")));

    public override Task<ValidateStateStoreConfig.Types.Response> ValidateStateStoreConfig(ValidateStateStoreConfig.Types.Request request, ServerCallContext context) =>
        Task.FromResult(new ValidateStateStoreConfig.Types.Response());

    public override Task<ConfigureStateStore.Types.Response> ConfigureStateStore(ConfigureStateStore.Types.Request request, ServerCallContext context) =>
        Task.FromException<ConfigureStateStore.Types.Response>(new RpcException(new Status(StatusCode.Unimplemented, "State stores are not supported.")));

    public override Task ReadStateBytes(ReadStateBytes.Types.Request request, IServerStreamWriter<ReadStateBytes.Types.ResponseChunk> responseStream, ServerCallContext context) =>
        Task.FromException(new RpcException(new Status(StatusCode.Unimplemented, "State stores are not supported.")));

    public override Task<WriteStateBytes.Types.Response> WriteStateBytes(IAsyncStreamReader<WriteStateBytes.Types.RequestChunk> requestStream, ServerCallContext context) =>
        Task.FromException<WriteStateBytes.Types.Response>(new RpcException(new Status(StatusCode.Unimplemented, "State stores are not supported.")));

    public override Task<LockState.Types.Response> LockState(LockState.Types.Request request, ServerCallContext context) =>
        Task.FromException<LockState.Types.Response>(new RpcException(new Status(StatusCode.Unimplemented, "State stores are not supported.")));

    public override Task<UnlockState.Types.Response> UnlockState(UnlockState.Types.Request request, ServerCallContext context) =>
        Task.FromException<UnlockState.Types.Response>(new RpcException(new Status(StatusCode.Unimplemented, "State stores are not supported.")));

    public override Task<GetStates.Types.Response> GetStates(GetStates.Types.Request request, ServerCallContext context) =>
        Task.FromException<GetStates.Types.Response>(new RpcException(new Status(StatusCode.Unimplemented, "State stores are not supported.")));

    public override Task<DeleteState.Types.Response> DeleteState(DeleteState.Types.Request request, ServerCallContext context) =>
        Task.FromException<DeleteState.Types.Response>(new RpcException(new Status(StatusCode.Unimplemented, "State stores are not supported.")));

    public override Task<StopProvider.Types.Response> StopProvider(StopProvider.Types.Request request, ServerCallContext context)
    {
        applicationLifetime.StopApplication();
        return Task.FromResult(new StopProvider.Types.Response());
    }

    private IResource GetResource(string typeName) =>
        provider.Resources.TryGetValue(typeName, out var resource)
            ? resource
            : throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown resource type '{typeName}'."));

    private IDataSource GetDataSource(string typeName) =>
        provider.DataSources.TryGetValue(typeName, out var dataSource)
            ? dataSource
            : throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown data source type '{typeName}'."));

    private IListResource GetListResource(string typeName) =>
        provider.ListResources.TryGetValue(typeName, out var listResource)
            ? listResource
            : throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown list resource type '{typeName}'."));

    private static Tfplugin6.Diagnostic ToProtocolDiagnostic(Diagnostics.Diagnostic diagnostic) =>
        new()
        {
            Severity = diagnostic.Severity == DiagnosticSeverity.Warning
                ? Tfplugin6.Diagnostic.Types.Severity.Warning
                : Tfplugin6.Diagnostic.Types.Severity.Error,
            Summary = diagnostic.Summary,
            Detail = diagnostic.Detail,
            Attribute = diagnostic.Attribute is null ? null : ToProtocolAttributePath(diagnostic.Attribute),
        };

    private static Tfplugin6.AttributePath ToProtocolAttributePath(Types.AttributePath path)
    {
        var attributePath = new Tfplugin6.AttributePath();

        foreach (var step in path.Steps)
        {
            var protocolStep = new Tfplugin6.AttributePath.Types.Step();

            switch (step.Selector)
            {
                case TerraformAttributePathSelector.AttributeName:
                    protocolStep.AttributeName = step.AttributeName!;
                    break;
                case TerraformAttributePathSelector.ElementKeyString:
                    protocolStep.ElementKeyString = step.ElementKeyString!;
                    break;
                case TerraformAttributePathSelector.ElementKeyInt:
                    protocolStep.ElementKeyInt = step.ElementIndex!.Value;
                    break;
            }

            attributePath.Steps.Add(protocolStep);
        }

        return attributePath;
    }

    private static ResourceIdentityData? ToProtocolIdentity(IResource resource, Types.DynamicValue state)
    {
        if (resource.IdentitySchema is null || state.IsNull || state.IsUnknown)
        {
            return null;
        }

        var identity = QueryListResults.BuildIdentity(state, resource.IdentitySchema);
        return new ResourceIdentityData
        {
            IdentityData = DynamicValueSerializer.EncodeDynamicValue(identity, resource.IdentitySchema.ValueType()),
        };
    }

    private static string ResolveImportId(IResource resource, ImportResourceState.Types.Request request)
    {
        if (!string.IsNullOrWhiteSpace(request.Id))
        {
            return request.Id;
        }

        if (resource.IdentitySchema is null || request.Identity is null)
        {
            return string.Empty;
        }

        var identity = DynamicValueSerializer.DecodeDynamicValue(
            request.Identity.IdentityData,
            resource.IdentitySchema.ValueType());
        var values = identity.AsObject();

        return values.TryGetValue("id", out var id) && id.IsKnown
            ? id.AsString()
            : string.Empty;
    }

    private static Types.DynamicValue DecodeOptionalDynamicValue(Tfplugin6.DynamicValue? value, TFType type)
    {
        if (value is null || (value.Msgpack.Length == 0 && value.Json.Length == 0))
        {
            return Types.DynamicValue.Null(type);
        }

        return DynamicValueSerializer.DecodeDynamicValue(value, type);
    }
}

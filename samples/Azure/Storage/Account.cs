using Azure.Validation;
using TerraformPlugin;
using TerraformPlugin.Schema;
using TerraformPlugin.Types;
using TerraformPlugin.Validation;

namespace Azure.Storage;

internal sealed class Account :
    AzureResource<Account, Storage>,
    IDataSource<Account, Account.Data>,
    IListResource<Account, Account.List>
{
    [NotEmpty]
    [MatchesConfiguredAccount]
    public required TF<string> AccountName { get; init; }

    [TFAttribute(Computed = true)]
    public TF<string> Id { get; init; }

    [TFAttribute(Computed = true)]
    public TF<string> BlobEndpoint { get; init; }

    public override async ValueTask<ModelResult<Account>> ReadAsync(
        ResourceContext<ProviderState> context,
        CancellationToken cancellationToken)
    {
        if (AccountName.IsNull || AccountName.IsUnknown)
            return new ModelResult<Account>(null, PrivateState: context.PrivateState);

        var diagnostics = Validate(context);

        if (diagnostics.Count > 0)
            return new ModelResult<Account>(null, PrivateState: context.PrivateState, Diagnostics: diagnostics);

        await context.ProviderState.ServiceClient.GetAccountInfoAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        return new ModelResult<Account>(
            Hydrate(context.ProviderState, AccountName.RequireValue()),
            PrivateState: context.PrivateState);
    }

    public override ValueTask<PlanResult<Account>> PlanAsync(
        Account? priorState,
        ResourceContext<ProviderState> context,
        CancellationToken cancellationToken)
    {
        if (AccountName.IsUnknown)
            return ValueTask.FromResult(
                new PlanResult<Account>(
                    new Account
                    {
                        AccountName = AccountName,
                        Id = TF<string>.Unknown(),
                        BlobEndpoint = TF<string>.Known(context.ProviderState.ServiceClient.Uri.ToString().TrimEnd('/')),
                    },
                    PlannedPrivateState: context.PriorPrivateState));

        var diagnostics = Validate(context);

        if (diagnostics.Count > 0)
            return ValueTask.FromResult(
                new PlanResult<Account>(
                    null,
                    PlannedPrivateState: context.PriorPrivateState,
                    Diagnostics: diagnostics));

        return ValueTask.FromResult(
            new PlanResult<Account>(
                Hydrate(context.ProviderState, AccountName.RequireValue()),
                PlannedPrivateState: context.PriorPrivateState));
    }

    public override async ValueTask<ModelResult<Account>> ApplyAsync(
        Account? priorState,
        ResourceContext<ProviderState> context,
        CancellationToken cancellationToken)
    {
        var diagnostics = Validate(context);

        if (diagnostics.Count > 0)
            return new ModelResult<Account>(null, PrivateState: context.PlannedPrivateState, Diagnostics: diagnostics);

        await context.ProviderState.ServiceClient.GetAccountInfoAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        return new ModelResult<Account>(
            Hydrate(context.ProviderState, AccountName.RequireValue()),
            PrivateState: context.PlannedPrivateState);
    }

    public override ValueTask<ModelResult<Account>> DeleteAsync(
        Account? priorState,
        ResourceContext<ProviderState> context,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ModelResult<Account>(null, PrivateState: context.PlannedPrivateState));

    public override ValueTask<ImportResult<Account>> ImportAsync(
        string id,
        ResourceContext<ProviderState> context,
        CancellationToken cancellationToken)
    {
        if (!TryParseLeafResourceName(id, out var accountName))
            accountName = id;

        var candidate = new Account { AccountName = TF<string>.Known(accountName) };
        var diagnostics = Validator.Validate(candidate, context.ProviderState);

        if (diagnostics.Count > 0)
            return ValueTask.FromResult(new ImportResult<Account>([], diagnostics));

        return ValueTask.FromResult(
            new ImportResult<Account>([Hydrate(context.ProviderState, accountName)]));
    }

    internal readonly record struct Data([property: NotEmpty] TF<string> AccountName);

    public static async ValueTask<Account?> GetAsync(
        Data parameters,
        DataSourceContext context,
        CancellationToken cancellationToken)
    {
        var providerState = context.RequireProviderState<ProviderState>();
        var accountName = parameters.AccountName;
        var candidate = new Account { AccountName = accountName };

        if (Validator.Validate(candidate, providerState).Count > 0)
            return null;

        await providerState.ServiceClient.GetAccountInfoAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return Hydrate(providerState, accountName.RequireValue());
    }

    internal readonly record struct List();

    public static ValueTask<IReadOnlyList<Account>> ListAsync(
        List parameters,
        ListResourceContext context,
        CancellationToken cancellationToken)
    {
        var providerState = context.RequireProviderState<ProviderState>();
        return ValueTask.FromResult<IReadOnlyList<Account>>([Hydrate(providerState, providerState.AccountName)]);
    }

    private static Account Hydrate(ProviderState providerState, string accountName) =>
        new()
        {
            AccountName = TF<string>.Known(accountName),
            Id = TF<string>.Known(Storage.Instance.Account.FormatResourceId(accountName)),
            BlobEndpoint = TF<string>.Known(providerState.ServiceClient.Uri.ToString().TrimEnd('/')),
        };
}

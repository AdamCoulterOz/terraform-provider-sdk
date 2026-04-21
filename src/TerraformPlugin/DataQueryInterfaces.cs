namespace TerraformPlugin;

public interface IDataSource<TSelf, TParams>
    where TSelf : IDataSource<TSelf, TParams>
    where TParams : new()
{
    static abstract ValueTask<TSelf?> GetAsync(
        TParams parameters,
        DataSourceContext context,
        CancellationToken cancellationToken);
}

public interface IListResource<TSelf, TParams>
    where TSelf : IListResource<TSelf, TParams>
    where TParams : new()
{
    static abstract ValueTask<IReadOnlyList<TSelf>> ListAsync(
        TParams parameters,
        ListResourceContext context,
        CancellationToken cancellationToken);
}

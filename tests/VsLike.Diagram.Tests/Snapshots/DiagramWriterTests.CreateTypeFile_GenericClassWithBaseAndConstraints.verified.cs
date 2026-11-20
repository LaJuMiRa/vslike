namespace Shop.Data;

public abstract partial class Repository<T> : RepositoryBase, IDisposable
    where T : class, new()
{
}

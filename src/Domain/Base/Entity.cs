namespace Subscrio.Core.Domain.Base;

/// <summary>
/// Base Entity class for all domain entities
/// Entities are identified by a unique ID
/// </summary>
public abstract class Entity<T>
{
    public T Props { get; set; }
    private readonly long? _id;

    protected Entity(T props, long? id = null)
    {
        Props = props;
        _id = id;
    }

    public long? Id => _id;

    public bool Equals(Entity<T>? entity)
    {
        if (entity is null)
        {
            return false;
        }
        if (ReferenceEquals(this, entity))
        {
            return true;
        }
        if (_id is null || entity._id is null)
        {
            return false;
        }
        return _id == entity._id;
    }
}

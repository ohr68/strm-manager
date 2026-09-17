namespace StrmManager.Common.Domain.Abstractions;

public abstract class DomainEvent : IDomainEvent
{
    protected DomainEvent(Guid id, DateTime occurredOnUtc)
    {
        Id = id;
        OccurredOnUtc = occurredOnUtc;
    }

    public Guid Id { get; }

    public DateTime OccurredOnUtc { get; }
}

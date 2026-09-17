namespace StrmManager.Common.Domain.Abstractions;

public interface IDomainEvent
{
    Guid Id { get; }

    DateTime OccurredOnUtc { get; }
}

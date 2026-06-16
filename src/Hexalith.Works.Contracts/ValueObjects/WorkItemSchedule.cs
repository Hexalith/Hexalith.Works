namespace Hexalith.Works.Contracts.ValueObjects;

public sealed record WorkItemSchedule(Priority? Priority = null, DateOnly? DueDate = null);

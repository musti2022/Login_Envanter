using EnterpriseInventory.Application.Abstractions;

namespace EnterpriseInventory.IntegrationTests.Persistence;

internal sealed class TestCurrentUser(string? userName) : ICurrentUser
{
    public string? UserName => userName;
}

/// <summary>A clock that only moves when a test moves it.</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

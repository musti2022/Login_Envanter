using EnterpriseInventory.Domain.Common;
using EnterpriseInventory.Domain.Users;
using static EnterpriseInventory.UnitTests.DomainModel.TestData;

namespace EnterpriseInventory.UnitTests.DomainModel;

public class UserSessionTests
{
    private static readonly SessionTimeouts Timeouts = new(
        Idle: TimeSpan.FromMinutes(20),
        Absolute: TimeSpan.FromHours(8),
        AccessRecheck: TimeSpan.FromMinutes(5),
        DirectoryOutageGrace: TimeSpan.FromMinutes(15));

    [Fact]
    public void A_new_session_is_active_and_its_access_was_just_confirmed()
    {
        var session = NewSession();

        Assert.True(session.IsActive);
        Assert.Equal(Now, session.StartedAt);
        Assert.Equal(Now, session.LastSeenAt);
        Assert.Equal(Now, session.LastAccessCheckAt);
        Assert.Equal(Now.AddHours(8), session.ExpiresAt);
        Assert.Equal("10.0.0.5", session.ClientAddress);
        Assert.Equal(SessionState.Active, session.Evaluate(Now.AddMinutes(4), Timeouts));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(64)]
    public void The_key_hash_must_be_a_sha256_digest(int length)
    {
        DomainAssert.Violates(DomainErrors.InvalidValue, () => UserSession.Start(Admin(), new byte[length], Now, Timeouts, null));
    }

    [Fact]
    public void Idle_timeout_starts_from_the_last_recorded_activity()
    {
        var session = NewSession();

        Assert.Equal(SessionState.AccessCheckDue, session.Evaluate(Now.AddMinutes(19), Timeouts));
        Assert.Equal(SessionState.IdleTimeoutReached, session.Evaluate(Now.AddMinutes(20), Timeouts));

        session.RecordAccessConfirmed(Now.AddMinutes(15));
        session.RecordActivity(Now.AddMinutes(15));
        Assert.Equal(SessionState.Active, session.Evaluate(Now.AddMinutes(19), Timeouts));
        Assert.Equal(SessionState.IdleTimeoutReached, session.Evaluate(Now.AddMinutes(35), Timeouts));
    }

    [Fact]
    public void Activity_is_written_at_most_once_a_minute()
    {
        var session = NewSession();

        Assert.False(session.RecordActivity(Now.AddSeconds(59)));
        Assert.Equal(Now, session.LastSeenAt);
        Assert.True(session.RecordActivity(Now.AddMinutes(1)));
        Assert.Equal(Now.AddMinutes(1), session.LastSeenAt);
    }

    [Fact]
    public void Absolute_timeout_ends_even_an_active_session()
    {
        var session = NewSession();
        for (var minute = 4; minute < 8 * 60; minute += 4)
        {
            session.RecordAccessConfirmed(Now.AddMinutes(minute));
            session.RecordActivity(Now.AddMinutes(minute));
        }

        Assert.Equal(SessionState.Active, session.Evaluate(Now.AddHours(8).AddSeconds(-1), Timeouts));
        Assert.Equal(SessionState.AbsoluteTimeoutReached, session.Evaluate(Now.AddHours(8), Timeouts));
    }

    [Fact]
    public void Access_is_rechecked_after_the_interval()
    {
        var session = NewSession();

        Assert.Equal(SessionState.Active, session.Evaluate(Now.AddMinutes(4).AddSeconds(59), Timeouts));
        Assert.Equal(SessionState.AccessCheckDue, session.Evaluate(Now.AddMinutes(5), Timeouts));

        session.RecordAccessConfirmed(Now.AddMinutes(5));
        Assert.Equal(SessionState.Active, session.Evaluate(Now.AddMinutes(9), Timeouts));
    }

    [Fact]
    public void While_the_directory_is_down_the_check_is_retried_each_minute_until_the_grace_period_ends()
    {
        var session = NewSession();

        Assert.False(session.RecordAccessCheckFailed(Now.AddMinutes(5), Timeouts));
        Assert.Equal(SessionState.Active, session.Evaluate(Now.AddMinutes(5).AddSeconds(30), Timeouts));
        Assert.Equal(SessionState.AccessCheckDue, session.Evaluate(Now.AddMinutes(6), Timeouts));

        session.RecordActivity(Now.AddMinutes(12));
        Assert.False(session.RecordAccessCheckFailed(Now.AddMinutes(19), Timeouts));
        Assert.True(session.RecordAccessCheckFailed(Now.AddMinutes(20), Timeouts));
    }

    [Fact]
    public void A_successful_check_ends_the_outage_state()
    {
        var session = NewSession();
        session.RecordAccessCheckFailed(Now.AddMinutes(5), Timeouts);

        session.RecordAccessConfirmed(Now.AddMinutes(6));

        Assert.Null(session.LastFailedAccessCheckAt);
        Assert.Equal(Now.AddMinutes(6), session.LastAccessCheckAt);
    }

    [Fact]
    public void Without_a_grace_period_the_first_failed_check_ends_the_session()
    {
        var session = NewSession();

        Assert.True(session.RecordAccessCheckFailed(Now.AddMinutes(5), Timeouts with { DirectoryOutageGrace = TimeSpan.Zero }));
    }

    [Fact]
    public void An_ended_session_keeps_its_first_reason_and_refuses_further_use()
    {
        var session = NewSession();

        session.End(SessionEndReason.SignedOut, Now.AddMinutes(3));
        session.End(SessionEndReason.IdleTimeout, Now.AddMinutes(30));

        Assert.False(session.IsActive);
        Assert.Equal(SessionEndReason.SignedOut, session.EndReason);
        Assert.Equal(Now.AddMinutes(3), session.EndedAt);
        Assert.Equal(SessionState.Ended, session.Evaluate(Now.AddMinutes(4), Timeouts));
        DomainAssert.Violates(DomainErrors.Session.Ended, () => session.RecordActivity(Now.AddMinutes(4)));
        DomainAssert.Violates(DomainErrors.Session.Ended, () => session.RecordAccessConfirmed(Now.AddMinutes(4)));
    }

    [Fact]
    public void End_reason_must_be_defined()
    {
        DomainAssert.Violates(DomainErrors.InvalidValue, () => NewSession().End((SessionEndReason)99, Now));
    }

    private static AdminUser Admin() => AdminUser.Create(Guid.NewGuid(), "ayse.admin", "Ayşe Yılmaz", Now);

    private static UserSession NewSession() => UserSession.Start(Admin(), new byte[UserSession.KeyHashLength], Now, Timeouts, " 10.0.0.5 ");
}

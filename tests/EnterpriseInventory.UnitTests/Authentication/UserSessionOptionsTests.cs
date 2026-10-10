using System.ComponentModel.DataAnnotations;
using EnterpriseInventory.Application.Authentication;

namespace EnterpriseInventory.UnitTests.Authentication;

public class UserSessionOptionsTests
{
    [Fact]
    public void Defaults_are_valid_and_match_the_documented_limits()
    {
        var options = new UserSessionOptions();

        Assert.Empty(Validate(options));
        Assert.Equal(TimeSpan.FromMinutes(20), options.Timeouts.Idle);
        Assert.Equal(TimeSpan.FromHours(8), options.Timeouts.Absolute);
        Assert.Equal(TimeSpan.FromMinutes(5), options.Timeouts.AccessRecheck);
        Assert.Equal(TimeSpan.FromMinutes(15), options.Timeouts.DirectoryOutageGrace);
    }

    [Theory]
    [InlineData(0, 8, 5, 15, nameof(UserSessionOptions.IdleTimeoutMinutes))]
    [InlineData(241, 8, 5, 15, nameof(UserSessionOptions.IdleTimeoutMinutes))]
    [InlineData(20, 0, 5, 15, nameof(UserSessionOptions.AbsoluteTimeoutHours))]
    [InlineData(20, 25, 5, 15, nameof(UserSessionOptions.AbsoluteTimeoutHours))]
    [InlineData(20, 8, 0, 15, nameof(UserSessionOptions.AccessRecheckMinutes))]
    [InlineData(20, 8, 61, 15, nameof(UserSessionOptions.AccessRecheckMinutes))]
    [InlineData(20, 8, 5, -1, nameof(UserSessionOptions.DirectoryOutageGraceMinutes))]
    [InlineData(120, 1, 5, 15, nameof(UserSessionOptions.IdleTimeoutMinutes))]
    public void Out_of_range_limits_are_refused(int idle, int absolute, int recheck, int grace, string member)
    {
        var options = new UserSessionOptions
        {
            IdleTimeoutMinutes = idle,
            AbsoluteTimeoutHours = absolute,
            AccessRecheckMinutes = recheck,
            DirectoryOutageGraceMinutes = grace,
        };

        Assert.Contains(Validate(options), result => result.MemberNames.Contains(member));
    }

    private static List<ValidationResult> Validate(UserSessionOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        if (results.Count == 0)
        {
            results.AddRange(options.Validate(new ValidationContext(options)));
        }

        return results;
    }
}

using EnterpriseInventory.Application;
using EnterpriseInventory.Application.Auditing;
using EnterpriseInventory.Domain.Auditing;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseInventory.UnitTests.Auditing;

public class AuditLogRequestTests
{
    private readonly IValidator<AuditLogRequest> _validator =
        new ServiceCollection().AddApplication().BuildServiceProvider().GetRequiredService<IValidator<AuditLogRequest>>();

    [Fact]
    public void Every_filter_together_is_accepted_and_read_into_the_criteria()
    {
        var request = new AuditLogRequest
        {
            Page = 2,
            PageSize = 50,
            EntityName = "asset",
            EntityId = " 42 ",
            AssetCode = " PC-IST ",
            Action = ["updated", "LocationChanged", "Updated"],
            UserName = "ayse",
            From = "2026-10-01T00:00:00+03:00",
            To = "2026-10-02T00:00:00Z",
            CorrelationId = "3f6c0b9e4a2d4c55a8a0f1d7c2e9b411",
        };

        Assert.True(_validator.Validate(request).IsValid);
        var criteria = AuditLogCriteria.FromRequest(request);

        Assert.Equal((2, 50), (criteria.Page, criteria.PageSize));
        Assert.Equal("Asset", criteria.EntityName);
        Assert.Equal("42", criteria.EntityId);
        Assert.Equal("PC-IST", criteria.AssetCode);
        Assert.Equal([AuditAction.Updated, AuditAction.LocationChanged], criteria.Actions);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 21, 0, 0, TimeSpan.Zero), criteria.From);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero), criteria.To);
    }

    [Fact]
    public void No_filter_lists_everything_from_the_first_page()
    {
        var criteria = AuditLogCriteria.FromRequest(new AuditLogRequest { UserName = "  ", AssetCode = "" });

        Assert.Equal((1, AuditLogRequest.DefaultPageSize), (criteria.Page, criteria.PageSize));
        Assert.Empty(criteria.Actions);
        Assert.All(
            new object?[] { criteria.EntityName, criteria.EntityId, criteria.AssetCode, criteria.UserName, criteria.From, criteria.To, criteria.CorrelationId },
            Assert.Null);
    }

    [Theory]
    [InlineData("2026-10-01T00:00:00Z")]
    [InlineData("2026-10-01T00:00:00.1234567+03:00")]
    [InlineData("2026-10-01T23:59-05:30")]
    public void Moments_with_an_offset_are_accepted(string text)
    {
        Assert.True(_validator.Validate(new AuditLogRequest { From = text }).IsValid);
        Assert.NotNull(AuditLogCriteria.FromRequest(new AuditLogRequest { From = text }).From);
    }

    [Theory]
    [InlineData("2026-10-01")]
    [InlineData("2026-10-01T00:00:00")]
    [InlineData("01.10.2026 00:00 +03:00")]
    [InlineData("2026-13-01T00:00:00Z")]
    [InlineData("yarın")]
    public void Moments_without_an_offset_or_in_another_format_are_refused(string text)
    {
        var error = Assert.Single(_validator.Validate(new AuditLogRequest { From = text }).Errors);

        Assert.Equal(nameof(AuditLogRequest.From), error.PropertyName);
        Assert.StartsWith("Başlangıç zamanı geçersiz.", error.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Asset", true)]
    [InlineData("ADMINUSER", true)]
    [InlineData("Employee", false)]
    [InlineData("Asset;DROP", false)]
    public void Only_the_audited_kinds_of_record_are_accepted(string entityName, bool valid) =>
        Assert.Equal(valid, _validator.Validate(new AuditLogRequest { EntityName = entityName }).IsValid);

    [Fact]
    public void Text_filters_are_limited_in_length_and_refuse_control_characters()
    {
        Assert.False(_validator.Validate(new AuditLogRequest { UserName = new string('a', 257) }).IsValid);
        Assert.False(_validator.Validate(new AuditLogRequest { UserName = "ayse\u0000" }).IsValid);
        Assert.False(_validator.Validate(new AuditLogRequest { AssetCode = new string('A', 51) }).IsValid);
        Assert.False(_validator.Validate(new AuditLogRequest { CorrelationId = new string('c', 65) }).IsValid);
        Assert.True(_validator.Validate(new AuditLogRequest { UserName = new string('a', 256), AssetCode = new string('A', 50) }).IsValid);
    }

    [Fact]
    public void The_end_of_the_window_must_come_after_its_start()
    {
        var sameMoment = new AuditLogRequest { From = "2026-10-01T03:00:00+03:00", To = "2026-10-01T00:00:00Z" };

        var error = Assert.Single(_validator.Validate(sameMoment).Errors);

        Assert.Equal(nameof(AuditLogRequest.To), error.PropertyName);
        Assert.Equal("Bitiş zamanı başlangıç zamanından sonra olmalıdır.", error.ErrorMessage);
    }
}

using EnterpriseInventory.Application.Assets;
using EnterpriseInventory.Domain.Assets;
using Microsoft.Extensions.Logging;

namespace EnterpriseInventory.UnitTests.Assets;

/// <summary>
/// Day 27: only committed changes are announced, writes that changed nothing are not, and a failing notifier never
/// turns a committed change into a failure.
/// </summary>
public class AssetChangePublisherTests
{
    private const string ReadVersion = "AAAAAAAAB9E=";
    private const string SavedVersion = "AAAAAAAAB9M=";

    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt = new(2026, 10, 9, 14, 30, 0, TimeSpan.Zero);

    private readonly RecordingNotifier _notifier = new();
    private readonly RecordingLogger _logger = new();

    [Theory]
    [InlineData(AssetChange.Updated)]
    [InlineData(AssetChange.Archived)]
    [InlineData(AssetChange.Assigned)]
    [InlineData(AssetChange.Returned)]
    [InlineData(AssetChange.LocationChanged)]
    public void A_saved_change_is_announced_with_the_assets_update_time(AssetChange change)
    {
        var result = AssetWriteResult.Succeeded(Asset(SavedVersion, UpdatedAt));

        Assert.Same(result, Publisher().Announce(change, result, ReadVersion));

        Assert.Equal([new AssetChanged(change, 42, UpdatedAt)], _notifier.Announced);
    }

    [Fact]
    public void A_new_asset_is_announced_with_its_creation_time()
    {
        Publisher().Announce(AssetChange.Created, AssetWriteResult.Succeeded(Asset(SavedVersion, updatedAt: null)));

        Assert.Equal([new AssetChanged(AssetChange.Created, 42, CreatedAt)], _notifier.Announced);
    }

    [Fact]
    public void A_refused_write_is_not_announced()
    {
        var refused = new[]
        {
            AssetWriteResult.Invalid(new Dictionary<string, string[]> { ["AssetCode"] = ["x"] }),
            AssetWriteResult.Duplicate(new Dictionary<string, string[]> { ["AssetCode"] = ["x"] }),
            AssetWriteResult.NotFound,
            AssetWriteResult.Conflict,
            AssetWriteResult.DirectoryUnavailable,
            AssetWriteResult.Rule("asset_archived"),
            AssetWriteResult.Succeeded(null),
        };

        foreach (var result in refused)
        {
            Assert.Same(result, Publisher().Announce(AssetChange.Updated, result, ReadVersion));
        }

        Assert.Empty(_notifier.Announced);
    }

    [Fact]
    public void A_write_that_changed_nothing_keeps_its_version_and_is_not_announced()
    {
        Publisher().Announce(AssetChange.Updated, AssetWriteResult.Succeeded(Asset(ReadVersion, UpdatedAt)), ReadVersion);
        Publisher().Announce(AssetChange.LocationChanged, AssetWriteResult.Succeeded(Asset(ReadVersion, UpdatedAt)), ReadVersion);

        Assert.Empty(_notifier.Announced);
    }

    [Fact]
    public void A_failing_notifier_is_logged_and_the_committed_change_still_succeeds()
    {
        _notifier.Failure = new InvalidOperationException("hub down");
        var result = AssetWriteResult.Succeeded(Asset(SavedVersion, UpdatedAt));

        var returned = Publisher().Announce(AssetChange.Assigned, result, ReadVersion);

        Assert.Same(result, returned);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(_notifier.Failure, entry.Exception);
    }

    private AssetChangePublisher Publisher() => new(_notifier, _logger);

    private static AssetDetails Asset(string rowVersion, DateTimeOffset? updatedAt)
    {
        var reference = new NamedReference(1, "Ad");
        return new AssetDetails(
            42, "DMR-0042", null, AssetType.Laptop, AssetStatus.Available, null, null, reference, reference, reference, reference, null, null,
            IsArchived: false, CreatedAt, "ayse.admin", updatedAt, updatedAt is null ? null : "ayse.admin", rowVersion);
    }

    private sealed class RecordingNotifier : IAssetChangeNotifier
    {
        public List<AssetChanged> Announced { get; } = [];

        public Exception? Failure { get; set; }

        public void Notify(AssetChanged change)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Announced.Add(change);
        }
    }

    private sealed class RecordingLogger : ILogger<AssetChangePublisher>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, exception));
    }
}

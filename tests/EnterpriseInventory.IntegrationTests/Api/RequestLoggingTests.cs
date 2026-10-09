using System.Net;

namespace EnterpriseInventory.IntegrationTests.Api;

public class RequestLoggingTests
{
    [Fact]
    public async Task Request_log_lines_go_to_the_configured_sinks_with_the_correlation_id()
    {
        // Also: successful health probes are left out of the log, rejected health requests are not.
        var logFile = Path.Combine(Path.GetTempPath(), $"ei-requests-{Guid.NewGuid():N}.log");
        try
        {
            string correlationId;
            await using (var api = new TestApiFactory(settings: new Dictionary<string, string?>
            {
                ["Serilog:WriteTo:0:Name"] = "File",
                ["Serilog:WriteTo:0:Args:path"] = logFile,
                ["Serilog:WriteTo:0:Args:outputTemplate"] = "{CorrelationId}|{Message:lj}{NewLine}",
            }))
            {
                using var client = api.CreateAnonymousClient();
                using var response = await client.GetAsync(new Uri("/api/does-not-exist", UriKind.Relative));
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                correlationId = response.Headers.GetValues("X-Correlation-ID").Single();
                (await client.GetAsync(new Uri("/api/health/live", UriKind.Relative))).Dispose();
                (await client.GetAsync(new Uri("/api/health", UriKind.Relative))).Dispose();
            }

            // Disposing the host flushes its logger.
            var lines = await File.ReadAllLinesAsync(logFile);
            Assert.Contains(lines, line => line.StartsWith($"{correlationId}|HTTP GET /api/does-not-exist responded 401", StringComparison.Ordinal));
            Assert.Contains(lines, line => line.Contains("HTTP GET /api/health responded 401", StringComparison.Ordinal));
            Assert.DoesNotContain(lines, line => line.Contains("HTTP GET /api/health/live responded", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(logFile);
        }
    }
}

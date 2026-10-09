using System.Globalization;
using EnterpriseInventory.Api.Health;
using EnterpriseInventory.Api.Http;
using EnterpriseInventory.Api.Security;
using EnterpriseInventory.Application;
using EnterpriseInventory.Application.Abstractions;
using EnterpriseInventory.Infrastructure;
using Serilog;
using Serilog.Events;

// Logs startup failures until the host's own logger, configured from appsettings, takes over.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // The host gets its own logger instead of sharing the static one, so several hosts in one process
    // (integration tests) never touch each other's logger.
    builder.Host.UseSerilog(
        (context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext(),
        preserveStaticLogger: true);

    builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

    builder.Services.AddApiProblemDetails();
    builder.Services.AddApiSecurity();
    builder.Services.AddApiRateLimiting(builder.Configuration);
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    // No CORS: the React app, /api and /hubs are served from the same origin, so cross-origin calls are refused.
    var app = builder.Build();

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }

    app.UseHttpsRedirection();
    app.UseMiddleware<SecurityHeadersMiddleware>();
    app.UseSerilogRequestLogging(options =>
    {
        // Without this the middleware writes to the static startup logger, which has no file sink.
        options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
        options.GetLevel = RequestLogLevel;
    });
    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();

    app.MapApiHealthChecks();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

// Successful health probes run every few seconds; logging them at Verbose keeps them out of the normal log.
// Rejected health requests (401, 403, 429) stay visible.
static LogEventLevel RequestLogLevel(HttpContext context, double elapsedMilliseconds, Exception? exception) =>
    exception is not null || context.Response.StatusCode >= StatusCodes.Status500InternalServerError ? LogEventLevel.Error
    : context.Request.Path.StartsWithSegments("/api/health") && context.Response.StatusCode < StatusCodes.Status400BadRequest ? LogEventLevel.Verbose
    : LogEventLevel.Information;

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;

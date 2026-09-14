using System.Threading.RateLimiting;
using FluentValidation;
using HackerNews.Api.Exceptions;
using HackerNews.Api.Features.Story.Options;
using HackerNews.Api.Features.Story.Services;
using HackerNews.Api.Features.Story.Services.Interfaces;
using HackerNews.Api.Shared.Behaviors;
using HackerNews.Api.Shared.Options;
using MediatR;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

const string CorsPolicyName = "Frontend";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

// RFC 7807 responses for every failure, produced by GlobalExceptionHandler.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.Configure<StoryCacheOptions>(
    builder.Configuration.GetSection(StoryCacheOptions.SectionName));

// CQRS. AddMediatR ships in the MediatR package itself; the old
// MediatR.Extensions.Microsoft.DependencyInjection package is deprecated and not referenced.
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);

    // MediatR 14 is dual-licensed. Without a key it still runs unrestricted, but logs a warning
    // saying production use requires a licence. Supplied via config so an App Service setting is
    // enough — no code change, and no key committed to git.
    if (builder.Configuration["MediatR:LicenseKey"] is { Length: > 0 } licenseKey)
    {
        cfg.LicenseKey = licenseKey;
    }
});

builder.Services.AddValidatorsFromAssembly(typeof(Program).Assembly);

// Registration order is execution order: logging wraps validation wraps the handler.
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

builder.Services
    .AddHttpClient<IHackerNewsClient, HackerNewsClient>(client =>
    {
        client.BaseAddress = new Uri(
            builder.Configuration["HackerNews:BaseUrl"]
            ?? "https://hacker-news.firebaseio.com/v0/");
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .AddStandardResilienceHandler();

builder.Services.AddSingleton<IStorySnapshotCache, StorySnapshotCache>();
builder.Services.AddSingleton<IStorySnapshotRefresher, StorySnapshotRefresher>();
builder.Services.AddHostedService<StorySnapshotBackgroundService>();

// Config-driven so the deployed origin is an App Service setting, never a code change.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy(CorsPolicyName, policy =>
{
    if (allowedOrigins.Length > 0)
    {
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    }
}));

builder.Services.Configure<RateLimitingOptions>(
    builder.Configuration.GetSection(RateLimitingOptions.SectionName));

builder.Services.Configure<NetworkOptions>(
    builder.Configuration.GetSection(NetworkOptions.SectionName));

// Configured from NetworkOptions rather than read straight off builder.Configuration, so the
// values come from the final configuration of the built host — the same reason RateLimitingOptions
// is resolved per request, and what lets a test boot the app as though it were behind a proxy.
builder.Services.AddOptions<ForwardedHeadersOptions>()
    .Configure<IOptions<NetworkOptions>>((forwarded, network) =>
    {
        // None makes the middleware a no-op, so the pipeline stays the same shape either way.
        forwarded.ForwardedHeaders = network.Value.TrustForwardedHeaders
            ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            : ForwardedHeaders.None;

        forwarded.ForwardLimit = network.Value.TrustedProxyHops;

        // App Service's front end has no fixed address to allow-list. Safe to leave empty only
        // because ForwardLimit caps how far in from the right we read, and those rightmost entries
        // are the ones the proxy itself wrote — a client cannot append past its own proxy.
        forwarded.KnownIPNetworks.Clear();
        forwarded.KnownProxies.Clear();
    });

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        // Resolved per request rather than captured at startup, so the configured value is the
        // one the built host actually has.
        var limit = context.RequestServices
            .GetRequiredService<IOptions<RateLimitingOptions>>().Value.PermitLimitPerMinute;

        // RemoteIpAddress is the real caller only because UseForwardedHeaders has already run.
        // Partitioning on the proxy's address would throttle every client as though they were one.
        return RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

var app = builder.Build();

// First in the pipeline. Everything downstream — the rate limiter above all — has to see the real
// caller rather than the proxy that forwarded them.
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    // Tells the browser never to try this origin over plain HTTP again, closing the first-request
    // downgrade window that a platform-level redirect cannot.
    //
    // Two things to know. It must sit after UseForwardedHeaders, because the middleware only emits
    // on a response the app believes is HTTPS — and behind App Service that is true only once
    // X-Forwarded-Proto is trusted (Network:TrustForwardedHeaders). And there is deliberately no
    // UseHttpsRedirection here: with forwarded headers untrusted, Kestrel sees plain HTTP behind
    // the front end, so redirecting to HTTPS would bounce straight back and loop forever. The
    // redirect belongs to the platform ("HTTPS Only" on App Service), which sees the real scheme.
    app.UseHsts();
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(CorsPolicyName);
app.UseRateLimiter();

app.MapControllers();

// Exempt from rate limiting on purpose. App Service marks an instance unhealthy on a failed probe
// and recycles it, so a 429 here would take the instance down — and recycle the warm snapshot with
// it — at exactly the moment the instance is busiest.
app.MapHealthChecks("/health").DisableRateLimiting();

await app.RunAsync();

/// <summary>Exposed so integration tests can boot the real app via WebApplicationFactory.</summary>
public partial class Program;

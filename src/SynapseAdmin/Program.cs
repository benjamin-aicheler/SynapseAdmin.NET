using SynapseAdmin.Components;
using LibMatrix.Services;
using SynapseAdmin.Services;
using SynapseAdmin.Interfaces;
using MudBlazor.Services;
using Microsoft.AspNetCore.Components.Authorization;

using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using MudBlazor.Translations;
using Serilog;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using SynapseAdmin.Extensions;
using SynapseAdmin.Infrastructure.DataProtection;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Infrastructure.Gateways;

var builder = WebApplication.CreateBuilder(args);

// Add Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ISessionBridgeService, SessionBridgeService>();
builder.Services.AddSingleton<ILoginRateLimiter, LoginRateLimiter>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var dpBuilder = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "keys")));

var passphrase = builder.Configuration["DP_PASSPHRASE"];
if (!string.IsNullOrEmpty(passphrase))
{
    dpBuilder.AddKeyManagementOptions(options => options.XmlEncryptor = new AesXmlEncryptor(passphrase));
}
else
{
    Log.Warning("DP_PASSPHRASE is not configured or empty. Data Protection keys will be stored UNENCRYPTED on disk.");
}

var trustedProxiesConfig = builder.Configuration["TRUSTED_PROXIES"];
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();

    if (!string.IsNullOrWhiteSpace(trustedProxiesConfig))
    {
        var entries = trustedProxiesConfig.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var entry in entries)
        {
            if (System.Net.IPNetwork.TryParse(entry, out var network))
            {
                options.KnownIPNetworks.Add(network);
            }
            else if (IPAddress.TryParse(entry, out var address))
            {
                options.KnownProxies.Add(address);
            }
            else
            {
                Log.Warning("Invalid entry '{ProxyEntry}' in TRUSTED_PROXIES configuration; ignoring.", entry.SanitizeForLogging());
            }
        }
    }

    // Default to loopback and private networks (RFC 1918 / RFC 4193) so Docker bridge/reverse proxies work without trusting arbitrary public peers
    if (options.KnownIPNetworks.Count == 0 && options.KnownProxies.Count == 0)
    {
        options.KnownProxies.Add(IPAddress.Loopback);
        options.KnownProxies.Add(IPAddress.IPv6Loopback);
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("127.0.0.0/8"));
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("fc00::/7"));
    }
});

builder.Services.AddRoryLibMatrixServices(new RoryLibMatrixConfiguration {
    AppName = "SynapseAdmin.NET"
});

builder.Services.AddLocalization();
builder.Services.AddMudTranslations();

builder.Services.AddScoped<IMatrixAuthGateway, MatrixAuthGateway>();

builder.Services.AddScoped<IMatrixSessionService, MatrixSessionService>();
builder.Services.AddScoped<IMediaService, MediaService>();
builder.Services.AddScoped<IRoomService, RoomService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IFederationService, FederationService>();
builder.Services.AddScoped<IEventReportService, EventReportService>();
builder.Services.AddScoped<IEventForensicsService, EventForensicsService>();
builder.Services.AddScoped<IRegistrationTokenService, RegistrationTokenService>();
builder.Services.AddScoped<IBackgroundUpdatesService, BackgroundUpdatesService>();
builder.Services.AddScoped<IThemeService, ThemeService>();
builder.Services.AddScoped<ITimeZoneService, TimeZoneService>();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, MatrixAuthenticationStateProvider>();
builder.Services.AddScoped<MatrixAuthenticationStateProvider>(sp => (MatrixAuthenticationStateProvider)sp.GetRequiredService<AuthenticationStateProvider>());

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => {
        options.LoginPath = "/login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/Media"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }
                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Path.StartsWithSegments("/Media"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }
                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddMudServices();

var app = builder.Build();

app.UseForwardedHeaders();

app.UseCookiePolicy(new CookiePolicyOptions
{
    HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always,
    Secure = CookieSecurePolicy.Always,
    MinimumSameSitePolicy = SameSiteMode.Lax
});

app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "SAMEORIGIN");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    await next();
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    if (!string.IsNullOrEmpty(app.Configuration["HTTPS_PORT"]) ||
        !string.IsNullOrEmpty(app.Configuration["ASPNETCORE_HTTPS_PORTS"]))
    {
        app.UseHttpsRedirection();
    }
}

var supportedCultures = new[] { "en-US", "de-DE", "fr-FR" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture(supportedCultures[0])
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

app.UseRequestLocalization(localizationOptions);

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AllowAnonymous();

app.Run();

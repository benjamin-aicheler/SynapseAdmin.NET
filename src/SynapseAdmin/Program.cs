using SynapseAdmin.Components;
using LibMatrix.Services;
using SynapseAdmin.Services;
using SynapseAdmin.Interfaces;
using MudBlazor.Services;
using Microsoft.AspNetCore.Components.Authorization;

using Microsoft.AspNetCore.Authentication.Cookies;
using MudBlazor.Translations;
using Serilog;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using SynapseAdmin.Infrastructure.DataProtection;

var builder = WebApplication.CreateBuilder(args);

// Add Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.
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

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Clear known networks and proxies so it works in Docker/Container environments
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddRoryLibMatrixServices(new RoryLibMatrixConfiguration {
    AppName = "SynapseAdmin.NET"
});

builder.Services.AddLocalization();
builder.Services.AddMudTranslations();

builder.Services.AddScoped<IMatrixSessionService, MatrixSessionService>();
builder.Services.AddScoped<IRoomService, RoomService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IFederationService, FederationService>();
builder.Services.AddScoped<IEventReportService, EventReportService>();
builder.Services.AddScoped<IRegistrationTokenService, RegistrationTokenService>();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, MatrixAuthenticationStateProvider>();
builder.Services.AddScoped<MatrixAuthenticationStateProvider>(sp => (MatrixAuthenticationStateProvider)sp.GetRequiredService<AuthenticationStateProvider>());

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => {
        options.LoginPath = "/login";
    });
builder.Services.AddAuthorization();

builder.Services.AddMudServices();

var app = builder.Build();

app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
    app.UseHttpsRedirection();
}

var supportedCultures = new[] { "en-US", "de-DE", "fr-FR" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture(supportedCultures[0])
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

app.UseRequestLocalization(localizationOptions);

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AllowAnonymous();

app.Run();

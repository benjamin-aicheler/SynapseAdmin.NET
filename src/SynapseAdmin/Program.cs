using SynapseAdmin.Components;
using LibMatrix.Services;
using SynapseAdmin.Services;
using MudBlazor.Services;
using Microsoft.AspNetCore.Components.Authorization;

using Microsoft.AspNetCore.Authentication.Cookies;
using MudBlazor.Translations;
using Microsoft.AspNetCore.Localization;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddRoryLibMatrixServices(new RoryLibMatrixConfiguration {
    AppName = "SynapseAdmin.NET"
});

builder.Services.AddLocalization();
builder.Services.AddMudTranslations();

builder.Services.AddScoped<MatrixSessionService>();
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

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

var supportedCultures = new[] { "en-US", "de-DE", "fr-FR" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture(supportedCultures[0])
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

app.UseRequestLocalization(localizationOptions);

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AllowAnonymous();

app.Run();

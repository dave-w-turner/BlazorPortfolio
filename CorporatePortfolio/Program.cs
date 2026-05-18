using CorporatePortfolio.Components;
using CorporatePortfolio.Data;
using CorporatePortfolio.Services;
using CorporatePortfolio.Services.DTO;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using MudBlazor.Services;
using Xceed.Words.NET;

var builder = WebApplication.CreateBuilder(args);

// Register the database connection string
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
});

authBuilder.AddIdentityCookies();

authBuilder.AddMicrosoftIdentityWebApp(options =>
{
    options.Instance = builder.Configuration["AzureAd:Instance"] ?? "https://microsoftonline.com";
    options.TenantId = builder.Configuration["AzureAd:TenantId"]!;
    options.ClientId = builder.Configuration["AzureAd:ClientId"]!;
    options.ClientSecret = builder.Configuration["AzureAd:ClientSecret"]!;

    options.SignInScheme = IdentityConstants.ExternalScheme;
    options.ResponseType = "code id_token";

    options.TokenValidationParameters.ValidateIssuer = false;
    options.TokenValidationParameters.NameClaimType = "name";

    options.Events.OnRemoteFailure = context =>
    {
        context.Response.Redirect("/");
        context.HandleResponse();
        return Task.CompletedTask;
    };
}, cookieScheme: null);

authBuilder.AddGoogle(options =>
{
    options.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
    options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;

    options.Events.OnRemoteFailure = context =>
    {
        context.Response.Redirect("/");
        context.HandleResponse();
        return Task.CompletedTask;
    };
});

if (builder.Configuration["Authentication:Facebook:AppId"] != null && builder.Configuration["Authentication:Facebook:AppSecret"] != null)
{
    authBuilder.AddFacebook(options =>
    {
        options.AppId = builder.Configuration["Authentication:Facebook:AppId"]!;
        options.AppSecret = builder.Configuration["Authentication:Facebook:AppSecret"]!;

        options.Events.OnRemoteFailure = context =>
        {
            context.Response.Redirect("/");
            context.HandleResponse();
            return Task.CompletedTask;
        };
    });
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddInteractiveServerComponents();

builder.Services.AddBlazorBootstrap();
builder.Services.AddMudServices();

builder.Services.AddControllers().AddMicrosoftIdentityUI();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddMemoryCache();

builder.Services.AddHttpClient("LocalAppClient");
builder.Services.AddScoped(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    return factory.CreateClient("LocalAppClient");
});

builder.Services.AddTransient(provider =>
{
    DocX document = DocX.Load("wwwroot/DavidTurner_Resume.docx");
    return new ResumeService(document);
});

// 3. Keep your specialized AI client completely isolated
builder.Services.AddHttpClient("OllamaClient", (provider, httpClient) =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var env = provider.GetRequiredService<IHostEnvironment>();

    if (!env.IsDevelopment())
    {
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config["Groq:ApiKey"]);
    }

    httpClient.BaseAddress = new Uri(config["OllamaServiceUrl"] ?? string.Empty);
    httpClient.Timeout = TimeSpan.FromMinutes(2);
});

builder.Services.AddSingleton(provider =>
{
    var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
    var ollamaClient = httpClientFactory.CreateClient("OllamaClient");

    return new ChatbotService(
        ollamaClient,
        provider.GetRequiredService<IHostEnvironment>().IsDevelopment(),
        provider.GetRequiredService<IConfiguration>()["OllamaModel"] ?? string.Empty,
        provider.GetRequiredService<ResumeService>(),
        provider.GetRequiredService<IMemoryCache>());
});

builder.Services.AddScoped<ChatState>();
builder.Services.AddScoped<AppState>();

builder.WebHost.UseStaticWebAssets();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapPost("/signin-oidc", async context =>
{
    await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.ChallengeAsync(
        context,
        Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectDefaults.AuthenticationScheme,
        new Microsoft.AspNetCore.Authentication.AuthenticationProperties());
})
.DisableAntiforgery()
.WithMetadata(new Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute());

app.MapControllers().DisableAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddInteractiveServerRenderMode();

app.Run();


using CorporatePortfolio.Components;
using Microsoft.EntityFrameworkCore;
using CorporatePortfolio.Data;
using CorporatePortfolio.Services;
using CorporatePortfolio.Services.DTO;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Identity.Web;
using MudBlazor.Services;
using Xceed.Words.NET;
using Microsoft.Identity.Web.UI;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

// Register the database connection string (adjust to your SQL Server setup)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true)
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

builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();

builder.Services.AddTransient(provider =>
{
    DocX document = DocX.Load("wwwroot/DavidTurner_Resume.docx");
    return new ResumeService(document);
});

builder.Services.AddTransient((provider) =>
{
    var modelName = provider.GetRequiredService<IConfiguration>()["OllamaModel"] ?? string.Empty;
    var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient();

    if (!provider.GetRequiredService<IHostEnvironment>().IsDevelopment())
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", provider.GetRequiredService<IConfiguration>()["Groq:ApiKey"]);

    httpClient.BaseAddress = new Uri($"{provider.GetRequiredService<IConfiguration>()["OllamaServiceUrl"]}" ?? string.Empty);
    httpClient.Timeout = TimeSpan.FromSeconds(30);

    return httpClient;
});

builder.Services.AddSingleton(provider =>
{
    var httpClient = provider.GetRequiredService<HttpClient>();
    httpClient.Timeout = TimeSpan.FromMinutes(2);

    return new ChatbotService(
        httpClient,
        provider.GetRequiredService<IHostEnvironment>().IsDevelopment(),
        provider.GetRequiredService<IConfiguration>()["OllamaModel"] ?? string.Empty,
        provider.GetRequiredService<ResumeService>(),
        provider.GetRequiredService<IMemoryCache>());
});

builder.Services.AddScoped<ChatState>();
builder.Services.AddScoped<AppState>();

builder.WebHost.UseStaticWebAssets();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseAuthentication();
app.UseAuthorization();

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseRouting()
    .UseAntiforgery()
    .UseEndpoints(r =>
    {
        r.MapDefaultControllerRoute();
    });

app.MapPost("/signin-oidc", async context =>
{
    await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.ChallengeAsync(
        context,
        OpenIdConnectDefaults.AuthenticationScheme,
        new Microsoft.AspNetCore.Authentication.AuthenticationProperties());
})
.DisableAntiforgery() // Strips the token check from this specific route
.WithMetadata(new Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute()); // Ensures public cloud azure calls pass through

app.MapControllers();
app.MapStaticAssets();
app.UseStaticFiles();
app.UseBlazorFrameworkFiles();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddInteractiveServerRenderMode();

app.Run();

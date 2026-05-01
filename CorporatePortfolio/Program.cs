using CorporatePortfolio.Components;
using CorporatePortfolio.HostedService;
using CorporatePortfolio.Services;
using CorporatePortfolio.Services.DTO;
using MudBlazor.Services;
using System.Text;
using Xceed.Words.NET;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddInteractiveServerComponents();

builder.Services.AddBlazorBootstrap();
builder.Services.AddMudServices();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddHttpClient();

builder.Services.AddTransient(provider =>
{
    DocX document = DocX.Load("DavidTurner_Resume.docx");
    return new ResumeService(document);
});

builder.Services.AddTransient((provider) =>
{
    var modelName = provider.GetRequiredService<IConfiguration>()["OllamaModel"] ?? string.Empty;
    var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient();

    if (!provider.GetRequiredService<IHostEnvironment>().IsDevelopment())
        httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(
            Encoding.ASCII.GetBytes((provider.GetRequiredService<IConfiguration>()["OllamaServiceCreds"] ?? string.Empty).Trim())));

    httpClient.BaseAddress = new Uri($"{provider.GetRequiredService<IConfiguration>()["OllamaServiceUrl"]}" ?? string.Empty);
    httpClient.DefaultRequestHeaders.Add("User-Agent", "C# App/1.0");

    return httpClient;
});

builder.Services.AddSingleton(provider =>
{
    var httpClient = provider.GetRequiredService<HttpClient>();
    httpClient.Timeout = TimeSpan.FromMinutes(1);

    return new ChatbotService(
        httpClient, 
        provider.GetRequiredService<IHostEnvironment>().IsDevelopment(), 
        provider.GetRequiredService<IConfiguration>()["OllamaModel"] ?? string.Empty,
        provider.GetRequiredService<ResumeService>().GetResumeText(provider.GetRequiredService<IHostEnvironment>().IsDevelopment()).Result);
});

builder.Services.AddHostedService((provider) =>
{
    var modelName = provider.GetRequiredService<IConfiguration>()["OllamaModel"] ?? string.Empty;
    var httpClient = provider.GetRequiredService<HttpClient>();
    httpClient.Timeout = TimeSpan.FromMinutes(10);
    return new OllamaWarmupService(httpClient, modelName);
});


builder.Services.AddScoped<ChatState>();

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

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseRouting()
    .UseAntiforgery()
    .UseEndpoints(r =>
    {
        r.MapDefaultControllerRoute();
    });

app.MapControllers();
app.MapStaticAssets();
app.UseStaticFiles();
app.UseBlazorFrameworkFiles();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddInteractiveServerRenderMode();

app.Run();

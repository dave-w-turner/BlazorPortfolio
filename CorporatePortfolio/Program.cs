using CorporatePortfolio.Components;
using CorporatePortfolio.Services;
using CorporatePortfolio.Services.DTO;
using Microsoft.Extensions.Caching.Memory;
using MudBlazor.Services;
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
builder.Services.AddMemoryCache();

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

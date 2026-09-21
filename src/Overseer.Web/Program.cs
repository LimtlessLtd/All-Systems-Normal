using Microsoft.Extensions.AI;
using OllamaSharp;
using Overseer.AI;
using Overseer.Simulation;
using Overseer.Web.Components;
using Overseer.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddRazorComponents()
    .AddInteractiveServerComponents();

var ollamaEndpoint =
    Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT")
    ?? builder.Configuration["AI:Ollama:Endpoint"]
    ?? "http://localhost:11434";

var ollamaModel =
    Environment.GetEnvironmentVariable("OLLAMA_MODEL_NAME")
    ?? builder.Configuration["AI:Ollama:Model"]
    ?? "qwen3:4b";

builder.Services.AddSingleton<IChatClient>(
    _ => new OllamaApiClient(new Uri(ollamaEndpoint), ollamaModel));

builder.Services.AddSingleton<RuleBasedAiDecisionService>();
builder.Services.AddSingleton<RuleBasedCrewGenerator>();
builder.Services.AddSingleton<RuleBasedOverseerMessageInterpreter>();
builder.Services.AddSingleton<IAiDecisionService, OllamaAiDecisionService>();
builder.Services.AddSingleton<IAiCrewGenerator, OllamaCrewGenerator>();
builder.Services.AddSingleton<IOverseerMessageInterpreter, OllamaOverseerMessageInterpreter>();

// A Blazor Server game session is per browser circuit. Do not share station
// state between different players by registering it as a singleton.
builder.Services.AddScoped<StationSession, GameSession>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
// The station console pages live in the shared Overseer.Web.UI library.
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(Overseer.Web.UI.Pages.Home).Assembly);

app.Run();

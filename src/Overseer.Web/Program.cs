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

var ollamaUri = new Uri(ollamaEndpoint);
builder.Services.AddSingleton(new OllamaRuntimeDiagnostics(
    ollamaUri,
    ollamaModel,
    capturePayloads: builder.Environment.IsDevelopment()));
builder.Services.AddSingleton<OllamaApiClient>(
    _ => new OllamaApiClient(ollamaUri, ollamaModel));
builder.Services.AddSingleton<IChatClient>(
    services => services.GetRequiredService<OllamaApiClient>());

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

// Local development must never fail silently into the deterministic fallback.
// Probe the exact configured Ollama endpoint once at startup and retain only
// payload-free operational status for /debug. Crew generation and every later
// model call are tracked by the same diagnostics store.
if (app.Environment.IsDevelopment())
{
    var diagnostics = app.Services.GetRequiredService<OllamaRuntimeDiagnostics>();
    var ollama = app.Services.GetRequiredService<OllamaApiClient>();
    var probeSequence = diagnostics.RecordStarted("startup health probe");

    try
    {
        if (await ollama.IsRunningAsync())
        {
            diagnostics.RecordResponse(probeSequence, "startup health probe");
            app.Logger.LogInformation(
                "Ollama is reachable at {Endpoint}; configured model: {Model}.",
                diagnostics.Endpoint,
                diagnostics.Model);
        }
        else
        {
            const string error = "The configured Ollama endpoint did not report a running server.";
            diagnostics.RecordFailure(probeSequence, "startup health probe", error);
            app.Logger.LogWarning(
                "Ollama is not reachable at {Endpoint}; NPC cognition will fall back until it becomes available.",
                diagnostics.Endpoint);
        }
    }
    catch (Exception exception)
    {
        diagnostics.RecordFailure(probeSequence, "startup health probe", exception);
        app.Logger.LogWarning(
            exception,
            "Ollama startup probe failed for {Endpoint}; NPC cognition will fall back until the provider can be reached.",
            diagnostics.Endpoint);
    }
}

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

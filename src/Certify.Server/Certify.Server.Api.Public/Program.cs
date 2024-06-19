using Certify.Server.API;

var builder = WebApplication.CreateBuilder(args);

#if ASPIRE
builder.AddServiceDefaults();
#endif

var startup = new Startup(builder.Configuration);

var results = startup.ConfigureServicesWithResults(builder.Services);

var app = builder.Build();

// log any relevant startup messages encountered during configure services
foreach (var result in results)
{
    if (result.IsSuccess)
    {
        app.Logger.LogInformation($"Startup: {result.Message}");
    }
    else
    {
        app.Logger.LogError($"Startup: {result.Message}");
    }
}

#if ASPIRE
app.MapDefaultEndpoints();
#endif

startup.Configure(app, builder.Environment);

app.Lifetime.ApplicationStarted.Register(async () => await startup.SetupStatusHubConnections(app));

app.Run();

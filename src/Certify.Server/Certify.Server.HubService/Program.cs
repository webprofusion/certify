using System.Runtime.InteropServices;
using Certify.Client;
using Certify.Management;
using Certify.Models;
using Certify.Models.Reporting;
using Certify.Server.Core;
using Certify.Server.Hub.Api.Extensions;
using Certify.Server.Hub.Api.Middleware;
using Certify.Server.Hub.Api.Services;
using Certify.Server.Hub.Api.SignalR;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Certify.Server.HubService.Extensions;
using Certify.Server.HubService.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Scalar.AspNetCore;
using Serilog;

List<ActionStep> _systemStatusItems = [];
void AddSystemStatusItem(string systemStatusCategory, string systemStatusKey, string title, string description, bool hasError = false, bool hasWarning = false) => _systemStatusItems.Add(new ActionStep(systemStatusKey, systemStatusCategory, title, description, hasError, hasWarning));

var hubServiceAssembly = typeof(Certify.Server.HubService.Services.CertifyDirectHubService).Assembly;

// allow settings to be loaded from the app data path, that way settings are preserved between re-installs, copy a default config so service starts on localhost:8080
var settingsPath = EnvironmentUtil.EnsuredAppDataPath();
var hubSettings = Path.Combine(settingsPath, "hubservice.json");

// set working directory so that when we are started as a service we can find our config
var cwd = Path.GetDirectoryName(hubServiceAssembly.Location);

if (cwd != null)
{
    System.Diagnostics.Debug.WriteLine($"Using working directory {cwd}");
    Directory.SetCurrentDirectory(cwd);

#if !DEBUG

    // copy the default settings if they don't exist yet, then generate a new JWT issuer secret
    var defaultHubSettings = Path.Combine(cwd, "default-settings.json");

    if (!File.Exists(hubSettings) && File.Exists(defaultHubSettings))
    {
        var content = File.ReadAllText(defaultHubSettings);

        var secret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        content = content.Replace("<replace jwt secret>", secret);

        // copy default config if it doesn't exist
        File.WriteAllText(hubSettings, content);
    }
#endif
}
else
{
    System.Diagnostics.Debug.WriteLine($"Could not determine working directory");
}

var builder = WebApplication.CreateBuilder(args);

// load optional config but ignore errors if it doesn't exist or is invalid, otherwise service will fail to start
if (File.Exists(hubSettings))
{
    try
    {
        builder.Configuration.AddJsonFile(hubSettings, optional: true, reloadOnChange: true);
    }
    catch (Exception ex)
    {
        // ignore errors loading config, we will log them later
        AddSystemStatusItem(
            SystemStatusCategories.HUB_API,
            SystemStatusKeys.HUB_API_STARTUP_CUSTOMCONFIG,
            title: "Hub API Service Custom Config",
            description: $"Error loading config file {hubSettings} - {ex}",
            hasError: true
        );
    }
}

// if windows, run as service, otherwise run as console app
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    builder.Services.AddWindowsService()
                    .AddHostedService<AgentBackgroundService>();
}
else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
{
    builder.Services.AddSystemd()
                .AddHostedService<AgentBackgroundService>();
}

builder.AddServiceDefaults();

AddSystemStatusItem(
    SystemStatusCategories.HUB_API,
    SystemStatusKeys.HUB_API_MODE,
    title: "Hub API with integrated Primary Instance",
    description: "Hub API using directly integrated primary service."
);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        builder.AllowAnyOrigin().AllowAnyMethod();
    });
});

builder.Services
    .AddLogging(loggingBuilder => loggingBuilder.AddSerilog(dispose: true))
    .AddMemoryCache()
    .AddTokenAuthentication(builder.Configuration)
    .AddAuthorization()
    .AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.WriteIndented = true;
    })
    .ConfigureApplicationPartManager((apm) =>
    {
        // remove service core assembly part, as controllers from this assembly are not needed in the hub API
        var serviceCore = (apm.ApplicationParts.FirstOrDefault(p => p.Name == "Certify.Server.Core") as AssemblyPart);
        if (serviceCore != null)
        {
            apm.ApplicationParts.Remove(serviceCore);
        }
    });

builder.Services
    .AddRouting(r => r.LowercaseUrls = true)
    .AddProblemDetails()
    .AddResponseCompression(opts =>
     {
         opts.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
             new[] { "application/octet-stream", "application/json" });
     })
    .AddSignalR(opt => opt.MaximumReceiveMessageSize = null).AddMessagePackProtocol();

var appDataPath = EnvironmentUtil.EnsuredAppDataPath("keys");

builder.Services
    .AddDataProtection(a =>
    {
        a.ApplicationDiscriminator = "certify";
    })
    .PersistKeysToFileSystem(new DirectoryInfo(appDataPath));

// configure OpenAPI
builder.Services.AddConfiguredOpenApiDocuments();

builder.Services.AddEndpointsApiExplorer();

// add an internal config store for hub api internal use (acme config etc)
var acmeStore = new Certify.Datastore.SQLite.SQLiteConfigurationStore("acme-server", customDbFileName: "acme-config");
await acmeStore.PerformMaintenance();

var acmeServerState = new AcmeServerConfig(acmeStore, "acme-server");
builder.Services.AddSingleton<AcmeServerConfig>(acmeServerState);
builder.Services.AddAcmeServices();

// Register proxy provider and HTTP client provider
builder.Services.AddSingleton<Certify.Shared.Net.IProxyProvider>(_ =>
    new Certify.Shared.Net.ProxyProvider(() =>
    {
        // For Hub Service, default to environment proxy
        return new Certify.Models.Preferences
    {
            ProxyMode = Certify.Models.ProxyMode.Environment,
            ProxyEnabled = true
        };
    }));

builder.Services.AddSingleton<Certify.Models.Providers.IHttpClientProvider, Certify.Shared.Net.HttpClientProvider>();

// setup public/hub api
builder.Services.AddSingleton<Certify.Management.ICertifyManager, Certify.Management.CertifyManager>();

builder.Services.AddTransient(typeof(ICertifyInternalApiClient), typeof(CertifyDirectHubService));

// setup server core
builder.Services.AddSingleton<IInstanceManagementStateProvider, InstanceManagementStateProvider>();

builder.Services.TryAddTransient<ManagedInstanceRequestAuthValidator>();

builder.Services.AddTransient<ManagementAPI>();

// used to directly talk back to the management server process instead of connecting back via SignalR
builder.Services.AddTransient<IInstanceManagementHub, InstanceManagementHub>();

builder.Services.AddTransient<IManagementServerClient, DirectManagementServerClient>();

builder.Services.AddHostedService<ManagementWorker>();

// build app and configure aspnet middleware

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();

    AddSystemStatusItem(
        SystemStatusCategories.HUB_API,
        SystemStatusKeys.HUB_API_STARTUP_ENVIRONMENT,
        title: "Development Mode",
        description: "Hub API is in Development mode."
    );
}
else
{
    AddSystemStatusItem(
        SystemStatusCategories.HUB_API,
        SystemStatusKeys.HUB_API_STARTUP_ENVIRONMENT,
        title: "Production Mode",
        description: "Hub API is in Production mode."
    );
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapDefaultEndpoints();

//app.UseHttpsRedirection();

app.UseResponseCompression();

// Rewrite /ui/* to / so SPA default file (index.html) is served
// https://learn.microsoft.com/aspnet/core/fundamentals/middleware
app.Use((context, next) =>
{
    if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
        && (context.Request.Path.StartsWithSegments("/ui") || context.Request.Path.StartsWithSegments("/authentication")))
    {
        context.Request.Path = "/";
    }

    return next(context);
});

// serve static files from wwwroot
app.UseDefaultFiles();

// Set up custom content types - associating file extension to MIME type
var provider = new FileExtensionContentTypeProvider();
// Add new mappings
provider.Mappings[".dat"] = "application/octet-stream";
provider.Mappings[".dll"] = "application/octet-stream";
provider.Mappings[".br"] = "application/x-br";
provider.Mappings[".image"] = "image/png";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = provider
});

// configure CORS
app.UseCors((p) =>
{
    p.AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader();
});

app.UseMiddleware<ManagedInstanceRequestAuthBodyHashMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHub<UserInterfaceStatusHub>("/api/internal/status");
app.MapHub<InstanceManagementHub>("/api/internal/managementhub");

app.MapDefaultControllerRoute().WithStaticAssets();

// publish scalar api docs endpoint in dev, e.g. https://localhost:44361/api/docs

// Enable middleware to serve generated OpenAPI document as a JSON endpoint.
app.MapOpenApi();

// Enable middleware to serve API docs
app.MapScalarApiReference("/api/docs/", options =>
{
    options
                    .WithTitle("Certify Management Hub API")
                    .WithTheme(ScalarTheme.Solarized)
                    .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
                    .WithOpenApiRoutePattern("/openapi/{documentName}.json")
                    .AddDocument("v1", "Internal and Public APIs", isDefault: true)
                    .AddDocument("v1-public", "Public API (/api)")
                    .AddDocument("v1-internal", "Internal Only API (/internal)");

});

AddSystemStatusItem(
    SystemStatusCategories.HUB_API,
    SystemStatusKeys.HUB_API_STARTUP_APIDOCS,
    title: "API Docs UI enabled",
    description: "Hub API docs available at /api/docs"
);

// configure initialization of UI status hub, backend management hub etc

var statusHubContext = app.Services.GetRequiredService<IHubContext<UserInterfaceStatusHub>>();
var instanceManagementHubContext = app.Services.GetRequiredService<IHubContext<InstanceManagementHub, IInstanceManagementHub>>();

// setup signalr message forwarding, message received from internal service will be resent to our connected clients via our own SignalR hub
var statusReporting = new UserInterfaceStatusHubReporting(statusHubContext);

var certifyManager = app.Services.GetRequiredService<ICertifyManager>();

certifyManager.EnableManagementHubBackend(isDirectHubBackend: true);

// initialize the CertifyManager instance, this includes initial setup of hub assigned instance id
await certifyManager.Init();

// setup direct management client, this tells the primary backend CertifyManager instance to talk directly to the management hub instead of via SignalR
var directServerClient = app.Services.GetRequiredService<IManagementServerClient>();

certifyManager.SetDirectManagementClient(directServerClient);

// wire up status reporting, include management hub cached state handlers for request progress state updates and item updates
certifyManager.SetStatusReporting(statusReporting);

var hubStateProvider = app.Services.GetRequiredService<IInstanceManagementStateProvider>();

bool HasManagedCertificateVersionChanged(ManagedCertificate? previousManagedCertificate, ManagedCertificate updatedManagedCertificate)
{
    if (updatedManagedCertificate == null || string.IsNullOrWhiteSpace(updatedManagedCertificate.Id))
    {
        return false;
    }

    if (!string.IsNullOrWhiteSpace(updatedManagedCertificate.CertificateThumbprintHash))
    {
        return !string.Equals(previousManagedCertificate?.CertificateThumbprintHash, updatedManagedCertificate.CertificateThumbprintHash, StringComparison.OrdinalIgnoreCase);
    }

    if (updatedManagedCertificate.DateRenewed.HasValue)
    {
        return previousManagedCertificate?.DateRenewed != updatedManagedCertificate.DateRenewed;
    }

    return false;
}

bool TryParseHubReference(string? reference, out string instanceId, out string managedCertificateId)
{
    instanceId = string.Empty;
    managedCertificateId = string.Empty;

    if (string.IsNullOrWhiteSpace(reference))
    {
        return false;
    }

    var normalized = reference.Trim().Replace(':', '/');
    var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

    if (parts.Length < 2)
    {
        return false;
    }

    instanceId = parts[0];
    managedCertificateId = parts[1];

    return !string.IsNullOrWhiteSpace(instanceId) && !string.IsNullOrWhiteSpace(managedCertificateId);
}

bool IsPushSubscriberForSource(ManagedCertificate managedCertificate, string sourceInstanceId, string sourceManagedCertificateId)
{
    var source = managedCertificate.ExternalSource;
    if (source?.IsEnabled != true)
    {
        return false;
    }

    if (!string.Equals(source.SourceType, ExternalCertificateSourceTypes.ManagementHub, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var retrievalMode = source.RetrievalMode ?? ExternalCertificateRetrievalModes.Pull;
    var isPushMode = string.Equals(retrievalMode, ExternalCertificateRetrievalModes.Push, StringComparison.OrdinalIgnoreCase)
        || string.Equals(retrievalMode, ExternalCertificateRetrievalModes.Auto, StringComparison.OrdinalIgnoreCase);

    if (!isPushMode)
    {
        return false;
    }

    return TryParseHubReference(source.ExternalReference, out var referencedInstanceId, out var referencedManagedCertificateId)
        && string.Equals(referencedInstanceId, sourceInstanceId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(referencedManagedCertificateId, sourceManagedCertificateId, StringComparison.OrdinalIgnoreCase);
}

List<(string TargetInstanceId, string TargetManagedCertificateId)> GetExternalPushSubscriptionTargets(string sourceInstanceId, ManagedCertificate updatedManagedCertificate)
{
    var targets = new List<(string TargetInstanceId, string TargetManagedCertificateId)>();
    var managedItemsByInstance = hubStateProvider.GetManagedInstanceItems();

    if (string.IsNullOrWhiteSpace(sourceInstanceId)
        || string.IsNullOrWhiteSpace(updatedManagedCertificate.Id))
    {
        return targets;
    }

    foreach (var instanceItems in managedItemsByInstance)
    {
        var targetInstanceId = instanceItems.Key;
        var items = instanceItems.Value?.Items;

        if (items == null || items.Count == 0)
        {
            continue;
        }

        foreach (var item in items)
        {
            if (!IsPushSubscriberForSource(item, sourceInstanceId, updatedManagedCertificate.Id))
            {
                continue;
            }

            if (targetInstanceId == sourceInstanceId && item.Id == updatedManagedCertificate.Id)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(item.Id))
            {
                targets.Add((targetInstanceId, item.Id));
            }
        }
    }

    return targets;
}

async Task NotifyExternalSubscribersOfManagedItemUpdateAsync(ManagedCertificate updatedManagedCertificate)
{
    try
    {
        if (string.IsNullOrWhiteSpace(updatedManagedCertificate.InstanceId) || string.IsNullOrWhiteSpace(updatedManagedCertificate.Id))
        {
            return;
        }

        var sourceVersion = updatedManagedCertificate.DateRenewed?.UtcDateTime.Ticks.ToString();
        var targets = GetExternalPushSubscriptionTargets(updatedManagedCertificate.InstanceId, updatedManagedCertificate);

        foreach (var target in targets)
        {
            var payload = new Certify.Models.Hub.ExternalManagedCertificateUpdate
            {
                ManagedCertificateId = target.TargetManagedCertificateId,
                SourceVersion = sourceVersion
            };

            var command = new Certify.Models.Hub.InstanceCommandRequest(Certify.Models.Hub.ManagementHubCommands.PushExternalManagedCertificateUpdate)
            {
                Value = System.Text.Json.JsonSerializer.Serialize(payload)
            };

            try
            {
                if (target.TargetInstanceId == hubStateProvider.GetManagementHubInstanceId())
                {
                    await certifyManager.PerformHubCommandWithResult(command);
                }
                else
                {
                    var connectionId = hubStateProvider.GetConnectionIdForInstance(target.TargetInstanceId);
                    if (string.IsNullOrWhiteSpace(connectionId))
                    {
                        app.Logger.LogWarning("Failed to queue external certificate push update for target {targetInstanceId} item {targetItemId}; no active connection exists.", target.TargetInstanceId, target.TargetManagedCertificateId);
                        continue;
                    }

                    await instanceManagementHubContext.Clients.Client(connectionId).SendCommandRequest(command);
                }

                app.Logger.LogInformation("Queued external certificate push update for target {targetInstanceId} item {targetItemId} from source {sourceInstanceId}/{sourceItemId}.", target.TargetInstanceId, target.TargetManagedCertificateId, updatedManagedCertificate.InstanceId, updatedManagedCertificate.Id);
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "Failed to queue external certificate push update for target {targetInstanceId} item {targetItemId} from source {sourceInstanceId}/{sourceItemId}.", target.TargetInstanceId, target.TargetManagedCertificateId, updatedManagedCertificate.InstanceId, updatedManagedCertificate.Id);
            }
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "NotifyExternalSubscribersOfManagedItemUpdateAsync failed for local managed item {sourceItemId}.", updatedManagedCertificate.Id);
    }
}

// inform the management hub of our assigned backend instance id, so we can tell when we are interacting with the mgmt hub vs a normal instance
hubStateProvider.SetManagementHubInstanceId(certifyManager.GetManagedInstanceInfo().InstanceId);

statusReporting.OnRequestProgressStateUpdated += (RequestProgressState state) =>
{

};

statusReporting.OnManagedCertificateUpdated += (ManagedCertificate item) =>
{
    if (item.InstanceId != null)
    {
        var previousManagedCertificate = hubStateProvider.GetManagedInstanceItems().TryGetValue(item.InstanceId, out var instanceItems)
            ? instanceItems.Items?.FirstOrDefault(i => string.Equals(i.Id, item.Id, StringComparison.OrdinalIgnoreCase))
            : null;

        hubStateProvider.UpdateCachedManagedInstanceItem(item.InstanceId, item);

        if (HasManagedCertificateVersionChanged(previousManagedCertificate, item))
        {
            _ = NotifyExternalSubscribersOfManagedItemUpdateAsync(item);
        }
    }
};

// start the server and watch for shutdown signals

app.Start();

app.Logger.LogInformation($"Server started {string.Join(";", app.Urls)}");

AddSystemStatusItem(
    SystemStatusCategories.HUB_API,
    SystemStatusKeys.HUB_API_STARTUP_URL,
    title: "API Urls Allocated",
    description: $"Hub API available at {string.Join(";", app.Urls)}"
);

foreach (var statusItem in _systemStatusItems)
{
    hubStateProvider.AddOrUpdateSystemStatusItem(statusItem);

    if (statusItem.HasError)
    {
        app.Logger.LogError($"{statusItem.Key} - {statusItem.Title} - {statusItem.Description}");
    }
    else if (statusItem.HasWarning)
    {
        app.Logger.LogWarning($"{statusItem.Key} - {statusItem.Title} - {statusItem.Description}");
    }
    else
    {
        app.Logger.LogInformation($"{statusItem.Key} - {statusItem.Title} - {statusItem.Description}");
    }
}

app.WaitForShutdown();

using System.Runtime.InteropServices;
using Certify.Client;
using Certify.Management;
using Certify.Models;
using Certify.Models.Reporting;
using Certify.Server.Core;
using Certify.Server.Hub.Api.Extensions;
using Certify.Server.Hub.Api.Middleware;
using Certify.Server.Hub.Api.Services;
using Certify.Server.Hub.Api.Services.Acme;
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
using Microsoft.Extensions.Primitives;
using Scalar.AspNetCore;
using Serilog;

List<ActionStep> _systemStatusItems = [];
void AddSystemStatusItem(string systemStatusCategory, string systemStatusKey, string title, string description, bool hasError = false, bool hasWarning = false) => _systemStatusItems.Add(new ActionStep(systemStatusKey, systemStatusCategory, title, description, hasError, hasWarning));

List<IDisposable> _kestrelCertificateWatchers = [];
IDisposable? _kestrelCertificateConfigReloadRegistration = null;

void ConfigureKestrelCertificateReloadWatchers(IConfiguration configuration, string configPath, string contentRootPath)
{
    // .Net should refresh certificates used by Kestrel when the certificate file changes
    // but currently it only detects settings file changes

    /*    
        •	Watches Kestrel certificate files referenced from hubservice.json.
        •	Supports both:
        •	Kestrel:Endpoints:*:Certificate:Path
        •	Kestrel:Certificates:*:Path
        •	Handles changed, created, deleted, and renamed certificate files.
        •	Debounces change events.
        •	Touches hubservice.json to trigger the existing configuration reload path so Kestrel reloads endpoint/certificate configuration.
        •	Refreshes watchers when hubservice.json itself reloads.
    */
    foreach (var watcher in _kestrelCertificateWatchers)
    {
        watcher.Dispose();
    }

    _kestrelCertificateWatchers.Clear();

    if (!File.Exists(configPath))
    {
        return;
    }

    var certificatePaths = configuration
        .GetSection("Kestrel:Endpoints")
        .GetChildren()
        .Select(endpoint => endpoint.GetSection("Certificate")["Path"])
        .Concat(configuration.GetSection("Kestrel:Certificates").GetChildren().Select(certificate => certificate["Path"]))
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => Path.GetFullPath(Environment.ExpandEnvironmentVariables(path!), contentRootPath))
        .Distinct(StringComparer.OrdinalIgnoreCase);

    foreach (var certificatePath in certificatePaths)
    {
        var directory = Path.GetDirectoryName(certificatePath);
        var fileName = Path.GetFileName(certificatePath);

        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(directory))
        {
            AddSystemStatusItem(
                SystemStatusCategories.HUB_API,
                $"kestrel-cert-watch-{certificatePath}",
                title: "Kestrel HTTPS Certificate Watch",
                description: $"Certificate file watcher not configured because path directory does not exist: {certificatePath}",
                hasWarning: true
            );

            continue;
        }

        var reloadTimer = new Timer(_ =>
        {
            try
            {
                // attempt to "touch" the hubservice.json file time so the kestrel reloads the config
                File.SetLastWriteTimeUtc(configPath, DateTime.UtcNow);
            }
            catch
            {
                // best effort; configuration reload will occur on the next hubservice.json change
            }
        });

        FileSystemEventHandler onChanged = (_, _) => reloadTimer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        RenamedEventHandler onRenamed = (_, _) => reloadTimer.Change(TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);

        var watcher = new FileSystemWatcher(directory, fileName)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
        };

        watcher.Changed += onChanged;
        watcher.Created += onChanged;
        watcher.Deleted += onChanged;
        watcher.Renamed += onRenamed;
        watcher.EnableRaisingEvents = true;

        _kestrelCertificateWatchers.Add(watcher);
        _kestrelCertificateWatchers.Add(reloadTimer);

        AddSystemStatusItem(
            SystemStatusCategories.HUB_API,
            $"kestrel-cert-watch-{certificatePath}",
            title: "Kestrel HTTPS Certificate Watch",
            description: $"Watching Kestrel HTTPS certificate file for reload: {certificatePath}"
        );
    }
}


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

    // Copy the default settings if they don't exist yet, then generate a new JWT issuer secret.
    // This runs in every build configuration: no JWT secret ships in appsettings.json, so a DEBUG
    // build which skipped this step would otherwise have no secret to sign or validate tokens with.
    var defaultHubSettings = Path.Combine(cwd, "default-settings.json");

    if (!File.Exists(hubSettings) && File.Exists(defaultHubSettings))
    {
        var content = File.ReadAllText(defaultHubSettings);

        content = content.Replace(HubJwtSecretProvisioning.SecretPlaceholder, HubJwtSecretProvisioning.GenerateSecret());

        // copy default config if it doesn't exist
        File.WriteAllText(hubSettings, content);
    }
}
else
{
    System.Diagnostics.Debug.WriteLine($"Could not determine working directory");
}

// An existing settings file may pre-date the JWT secret, or have had it removed. Generate and save one rather
// than leaving the service with no way to sign or validate tokens. Runs before the configuration is built so
// the newly saved value is picked up by the load below.
if (File.Exists(hubSettings))
{
    var jwtSecretResult = HubJwtSecretProvisioning.EnsureSecret(hubSettings);

    if (jwtSecretResult.Outcome != JwtSecretProvisioningOutcome.AlreadyPresent)
    {
        AddSystemStatusItem(
            SystemStatusCategories.HUB_API,
            SystemStatusKeys.HUB_API_STARTUP_JWTSECRET,
            title: "Hub API JWT Secret",
            description: jwtSecretResult.Message,
            hasError: jwtSecretResult.Outcome == JwtSecretProvisioningOutcome.Failed,
            hasWarning: jwtSecretResult.Outcome != JwtSecretProvisioningOutcome.Failed
        );
    }
}

var builder = WebApplication.CreateBuilder(args);

// load optional config but ignore errors if it doesn't exist or is invalid, otherwise service will fail to start
if (File.Exists(hubSettings))
{
    try
    {
        builder.Configuration.AddJsonFile(hubSettings, optional: true, reloadOnChange: true);

        ConfigureKestrelCertificateReloadWatchers(builder.Configuration, hubSettings, builder.Environment.ContentRootPath);
        _kestrelCertificateConfigReloadRegistration = ChangeToken.OnChange(
            () => ((IConfiguration)builder.Configuration).GetReloadToken(),
            () => ConfigureKestrelCertificateReloadWatchers(builder.Configuration, hubSettings, builder.Environment.ContentRootPath));
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
    .AddLogging(loggingBuilder =>
    {
        // Levels/overrides come from the Serilog section of appsettings.json / hubservice.json.
        // File output always goes under the standard app data logs path (e.g. C:\ProgramData\certify\logs).
        var logPath = Path.Combine(EnvironmentUtil.EnsuredAppDataPath("logs"), "hubservice-.log");

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(builder.Configuration)
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        loggingBuilder.AddSerilog(dispose: true);
    })
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
builder.Services.AddSingleton<ExternalSubscriberNotificationService>();

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

// Both hubs require an authenticated caller. The status hub carries managed certificate state for every
// connected instance, so an anonymous connection to it is a live feed of managed domains and config.
// Clients present their token via the access_token query string, which the JWT bearer middleware is
// configured to read for these two paths (see AuthenticationExtension).
app.MapHub<UserInterfaceStatusHub>("/api/internal/status").RequireAuthorization();
app.MapHub<InstanceManagementHub>("/api/internal/managementhub").RequireAuthorization();

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
var externalSubscriberNotificationService = app.Services.GetRequiredService<ExternalSubscriberNotificationService>();

// setup signalr message forwarding, message received from internal service will be resent to our connected clients via our own SignalR hub
var statusReporting = new UserInterfaceStatusHubReporting(statusHubContext);

var certifyManager = app.Services.GetRequiredService<ICertifyManager>();

certifyManager.EnableManagementHubBackend(isDirectHubBackend: true);

// wire up status reporting before init, so that diagnostics raised during startup (such as the data store being
// unreachable) reach connected clients
certifyManager.SetStatusReporting(statusReporting);

// initialize the CertifyManager instance, this includes initial setup of hub assigned instance id
await certifyManager.Init();

// setup direct management client, this tells the primary backend CertifyManager instance to talk directly to the management hub instead of via SignalR
var directServerClient = app.Services.GetRequiredService<IManagementServerClient>();

certifyManager.SetDirectManagementClient(directServerClient);

var hubStateProvider = app.Services.GetRequiredService<IInstanceManagementStateProvider>();

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

        if (externalSubscriberNotificationService.HasManagedCertificateVersionChanged(previousManagedCertificate, item))
        {
            _ = externalSubscriberNotificationService.NotifyExternalSubscribersOfManagedItemUpdateAsync(item);
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

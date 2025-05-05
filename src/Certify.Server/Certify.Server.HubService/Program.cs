using System.Reflection;
using System.Runtime.InteropServices;
using Certify.Client;
using Certify.Management;
using Certify.Models;
using Certify.Models.Reporting;
using Certify.Server.Core;
using Certify.Server.Hub.Api.Middleware;
using Certify.Server.Hub.Api.Services;
using Certify.Server.Hub.Api.SignalR;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Certify.Server.HubService.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;
using Serilog;
using Swashbuckle.AspNetCore.SwaggerGen;

List<ActionStep> _systemStatusItems = [];
void AddSystemStatusItem(string systemStatusCategory, string systemStatusKey, string title, string description, bool hasError = false, bool hasWarning = false) => _systemStatusItems.Add(new ActionStep(systemStatusKey, systemStatusCategory, title, description, hasError, hasWarning));

var assembly = typeof(Certify.Server.Hub.Api.Startup).Assembly;

// set working directory so that when we are started as a service we can find our config
var cwd = Path.GetDirectoryName(assembly.Location);
if (cwd != null)
{
    System.Diagnostics.Debug.WriteLine($"Using working directory {cwd}");
    Directory.SetCurrentDirectory(cwd);
}
else
{
    System.Diagnostics.Debug.WriteLine($"Could not determine working directory");
}

var builder = WebApplication.CreateBuilder(args);

// allow settings to be loaded from the app data path, that way settings are preserved between re-installs, copy a default config so service starts on localhost:8080
var settingsPath = EnvironmentUtil.EnsuredAppDataPath();
var hubSettings = Path.Combine(settingsPath, "hubservice.json");
var defaultHubSettings = Path.Combine(cwd, "default-settings.json");

#if !DEBUG
if (!File.Exists(hubSettings) && File.Exists(defaultHubSettings))
{
    // copy default config if it doesn't exist
    File.Copy(
        defaultHubSettings,
        hubSettings,
        false
    );
}
#endif

builder.Configuration.AddJsonFile(hubSettings, optional: true, reloadOnChange: true);

// if windows, run as service, otherwise run as console app
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    builder.Services.AddWindowsService()
                    .AddHostedService<WindowsBackgroundService>();
}

builder.AddServiceDefaults();

AddSystemStatusItem(
    SystemStatusCategories.HUB_API,
    SystemStatusKeys.HUB_API_MODE,
    title: "Hub API with integrated Primary Instance",
    description: "Hub API using directly integrated primary service."
);

// Add services to the container

var part = new AssemblyPart(assembly);

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
        .ConfigureApplicationPartManager(apm => apm.ApplicationParts.Add(part));

builder.Services
    .AddRouting(r => r.LowercaseUrls = true)
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

// configure OpenAPI, swagger and API explorer
builder.Services.AddOpenApi();

builder.Services.AddEndpointsApiExplorer();

// Register the Swagger generator, defining 1 or more Swagger documents
// https://docs.microsoft.com/en-us/aspnet/core/tutorials/getting-started-with-swashbuckle?view=aspnetcore-3.1&tabs=visual-studio
builder.Services.AddSwaggerGen(c =>
{

    // docs UI will be available at /docs

    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Certify Management Hub API",
        Version = "v1",
        Description = "The Certify Management Hub API provides a certificate services API for use in UI, devops, CI/CD, middleware etc. See certifytheweb.com for more details."
    });

    c.UseAllOfToExtendReferenceSchemas();

    c.DocInclusionPredicate((docName, apiDesc) =>
    {
        if (!apiDesc.TryGetMethodInfo(out MethodInfo methodInfo))
        {
            return false;
        }

        return methodInfo.DeclaringType.Namespace.StartsWith("Certify.Server.Hub.Api.Controllers");
    });

    // use the actual method names as the generated operation id
    c.CustomOperationIds(e =>
        $"{e.ActionDescriptor.RouteValues["action"]}"
    );

    // declare authorization method
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http
    });

    // set security requirement
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            }, new List<string>()
        }
    });

    // Set the comments path for the Swagger JSON and UI.
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.IncludeXmlComments(xmlPath);

    c.MapType<FileContentResult>(() =>
    {
        return new Microsoft.OpenApi.Models.OpenApiSchema
        {
            Type = "string",
            Format = "binary",
        };
    });
});

// setup public/hub api
builder.Services.AddSingleton<Certify.Management.ICertifyManager, Certify.Management.CertifyManager>();

builder.Services.AddTransient(typeof(ICertifyInternalApiClient), typeof(CertifyDirectHubService));

// setup server core
builder.Services.AddSingleton<IInstanceManagementStateProvider, InstanceManagementStateProvider>();

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
        description: $"Hub API is in Development mode."
    );
}
else
{
    AddSystemStatusItem(
        SystemStatusCategories.HUB_API,
        SystemStatusKeys.HUB_API_STARTUP_ENVIRONMENT,
        title: "Production Mode",
        description: $"Hub API is in Production mode."
    );
}

app.MapDefaultEndpoints();

app.UseHttpsRedirection();

app.UseResponseCompression();

// serve static files from wwwroot
app.UseDefaultFiles();

// Set up custom content types - associating file extension to MIME type
var provider = new FileExtensionContentTypeProvider();
// Add new mappings
provider.Mappings[".dat"] = "application/octet-stream";
provider.Mappings[".dll"] = "application/octet-stream";
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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHub<UserInterfaceStatusHub>("/api/internal/status");
app.MapHub<InstanceManagementHub>("/api/internal/managementhub");

app.MapDefaultControllerRoute().WithStaticAssets();

// publish scalar api docs endpoint in dev, e.g. https://localhost:44361/api/docs

// Enable middleware to serve generated Swagger as a JSON endpoint.
app.UseSwagger();

// Enable middleware to serve API docs
app.MapScalarApiReference("/api/docs/", options =>
{
    options
                    .WithTitle("Certify Management Hub API")
                    .WithTheme(ScalarTheme.Solarized)
                    .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
                    .WithOpenApiRoutePattern("/swagger/v1/swagger.json");

});

AddSystemStatusItem(
    SystemStatusCategories.HUB_API,
    SystemStatusKeys.HUB_API_STARTUP_SWAGGER,
    title: "API Docs UI enabled",
    description: $"Hub API docs available at /api/docs"
);

// configure initialization of UI status hub, backend management hub etc

var statusHubContext = app.Services.GetRequiredService<IHubContext<UserInterfaceStatusHub>>();

if (statusHubContext == null)
{
    throw new Exception("Status Hub not registered");
}

// setup signalr message forwarding, message received from internal service will be resent to our connected clients via our own SignalR hub
var statusReporting = new UserInterfaceStatusHubReporting(statusHubContext);

// wire up internal service to our hub
var managementServerClient = app.Services.GetService<DirectManagementServerClient>();

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

// inform the management hub of our assigned backend instance id, so we can tell when we are interacting with the mgmt hub vs a normal instance
hubStateProvider.SetManagementHubInstanceId(certifyManager.GetManagedInstanceInfo().InstanceId);

statusReporting.OnRequestProgressStateUpdated += (RequestProgressState state) =>
{

};

statusReporting.OnManagedCertificateUpdated += (ManagedCertificate item) =>
{
    if (item.InstanceId != null)
    {
        var mgmtHubState = app.Services.GetRequiredService<IInstanceManagementStateProvider>();
        mgmtHubState.UpdateCachedManagedInstanceItem(item.InstanceId, item);
    }
};

// start the server and watch for shutdown signals

app.Start();

System.Diagnostics.Debug.WriteLine($"Server started {string.Join(";", app.Urls)}");

AddSystemStatusItem(
    SystemStatusCategories.HUB_API,
    SystemStatusKeys.HUB_API_STARTUP_URL,
    title: "API Urls Allocated",
    description: $"Hub API available at {string.Join(";", app.Urls)}"
);

foreach (var statusItem in _systemStatusItems)
{
    hubStateProvider.AddOrUpdateSystemStatusItem(statusItem);
}

app.WaitForShutdown();

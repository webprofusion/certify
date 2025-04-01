using System.Reflection;
using Certify.Client;
using Certify.Management;
using Certify.Models;
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

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container

var assembly = typeof(Certify.Server.Hub.Api.Startup).Assembly;
var part = new AssemblyPart(assembly);

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

var appDataPath = EnvironmentUtil.CreateAppDataPath("keys");

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

// publish scalar api docs endpoint in dev, e.g. https://localhost:44361/scalar/
app.MapOpenApi();
app.MapScalarApiReference();

// Enable middleware to serve generated Swagger as a JSON endpoint.
app.UseSwagger();

// Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.),
// specifying the Swagger JSON endpoint.
app.UseSwaggerUI(c =>
{
    c.RoutePrefix = "docs";
    c.DocumentTitle = "Certify Management Hub API";
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Certify Management Hub API");
});

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
app.WaitForShutdown();

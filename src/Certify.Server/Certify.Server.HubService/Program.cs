using Certify.Client;
using Certify.Management;
using Certify.Server.Hub.Api.Middleware;
using Certify.Server.Hub.Api.Services;
using Certify.Server.Hub.Api.SignalR;
using Certify.Server.Hub.Api.SignalR.ManagementHub;
using Certify.Server.HubService.Services;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.StaticFiles;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container

var assembly = typeof(Certify.Server.Hub.Api.Startup).Assembly;
var part = new AssemblyPart(assembly);

builder.Services
    .AddMemoryCache()
    .AddTokenAuthentication(builder.Configuration)
    .AddAuthorization()
    .AddControllers()
        .ConfigureApplicationPartManager(apm => apm.ApplicationParts.Add(part));

builder.Services
    .AddRouting(r => r.LowercaseUrls = true)
    .AddSignalR(opt => opt.MaximumReceiveMessageSize = null)
    .AddMessagePackProtocol();

builder.Services.AddDataProtection(a =>
{
    a.ApplicationDiscriminator = "certify";
});

builder.Services.AddResponseCompression();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddLogging(loggingBuilder =>
        loggingBuilder.AddSerilog(dispose: true));

// setup public/hub api
builder.Services.AddSingleton<Certify.Management.ICertifyManager, Certify.Management.CertifyManager>();

builder.Services.AddTransient(typeof(ICertifyInternalApiClient), typeof(CertifyHubService));

// setup server core
builder.Services.AddSingleton<IInstanceManagementStateProvider, InstanceManagementStateProvider>();

builder.Services.AddTransient<ManagementAPI>();

// used to directly talk back to the management server process instead of connecting back via SignalR
builder.Services.AddTransient<IInstanceManagementHub, DirectInstanceManagementHub>();
builder.Services.AddTransient<IManagementServerClient, DirectManagementServerClient>();

builder.Services.AddHostedService<ManagementWorker>();

// build app

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

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

// configure CROS
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
app.UseResponseCompression();

var statusHubContext = app.Services.GetRequiredService<IHubContext<UserInterfaceStatusHub>>();

if (statusHubContext == null)
{
    throw new Exception("Status Hub not registered");
}

// setup signalr message forwarding, message received from internal service will be resent to our connected clients via our own SignalR hub
var statusReporting = new UserInterfaceStatusHubReporting(statusHubContext);

// wire up internal service to our hub

var certifyManager = app.Services.GetRequiredService<ICertifyManager>();
await certifyManager.Init();

var directServerClient = app.Services.GetRequiredService<IManagementServerClient>();
certifyManager.SetDirectManagementClient(directServerClient);

app.Start();

System.Diagnostics.Debug.WriteLine($"Server started {string.Join(";", app.Urls)}");
app.WaitForShutdown();

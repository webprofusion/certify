using System.Runtime.InteropServices;
using Certify.Server.Core;

var builder = WebApplication.CreateBuilder(args);

if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
{
    builder.Services.AddSystemd()
                .AddHostedService<StubBackgroundService>();
}

if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
{
    builder.Services.AddWindowsService()
                    .AddHostedService<StubBackgroundService>();
}

builder.Configuration.AddJsonFile("appsettings-core.json", optional: true, reloadOnChange: true);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings-core.Development.json", optional: true, reloadOnChange: true);
}

var serviceConfig = Certify.SharedUtils.ServiceConfigManager.GetAppServiceConfig();

if (serviceConfig.Host != null && serviceConfig.Port != 0)
{
    builder.WebHost.UseUrls($"http://{serviceConfig.Host}:{serviceConfig.Port}");
}
else
{
    // set default host and port
    builder.WebHost.UseUrls("http://localhost:9696");
}

builder.AddServiceDefaults();

var startup = new Startup(builder.Configuration);

await startup.ConfigureServices(builder.Services);

var app = builder.Build();

app.MapDefaultEndpoints();

startup.Configure(app, builder.Environment);

app.Run();

/// <summary>
/// Declare program as partial for reference in tests: https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-8.0
/// </summary>
public partial class Program { }

using System.Runtime.InteropServices;
using Certify.Models;
using Certify.Server.Core;

// Last-resort diagnostics: if any exception ever escapes to this point the process is about to be
// terminated by the runtime, so log full exception details before that happens. Without this, a crash
// only shows up as a generic "unhandled exception" Application Error in the Windows Event Log with no
// further detail.
AppDomain.CurrentDomain.UnhandledException += (s, e) =>
{
    try
    {
        var logPath = EnvironmentUtil.EnsuredAppDataPath("logs");
        System.IO.File.AppendAllText(System.IO.Path.Combine(logPath, "unhandled_exceptions.log"), $"{DateTime.Now}: IsTerminating={e.IsTerminating} : {e.ExceptionObject}\r\n");
    }
    catch
    {
        // best effort only, never throw from within the handler itself
    }
};

TaskScheduler.UnobservedTaskException += (s, e) =>
{
    try
    {
        var logPath = EnvironmentUtil.EnsuredAppDataPath("logs");
        System.IO.File.AppendAllText(System.IO.Path.Combine(logPath, "unhandled_exceptions.log"), $"{DateTime.Now}: Unobserved Task Exception : {e.Exception}\r\n");
    }
    catch
    {
        // best effort only, never throw from within the handler itself
    }

    e.SetObserved();
};

var builder = WebApplication.CreateBuilder(args);

if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
{
    builder.Services.AddSystemd()
                .AddHostedService<AgentBackgroundService>();
}
else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    builder.Services.AddWindowsService()
                    .AddHostedService<AgentBackgroundService>();
}

builder.Configuration.AddJsonFile("appsettings-core.json", optional: true, reloadOnChange: true);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddJsonFile("appsettings-core.Development.json", optional: true, reloadOnChange: true);
}

var serviceConfig = Certify.SharedUtils.ServiceConfigManager.GetAppServiceConfig();

// The service listens on exactly one transport, selected by "Transport" in serviceconfig.json:
// http by default, or the local named pipe, in which case http is not published at all. Named pipes
// are windows only, so a named pipe selection on any other platform falls back to http.
var namedPipeSelected = Certify.Shared.NamedPipeConnection.IsNamedPipeTransport(serviceConfig);

var endpointLog = new List<string>();

if (namedPipeSelected && !OperatingSystem.IsWindows())
{
    namedPipeSelected = false;
    endpointLog.Add("WARNING: the named pipe transport is only available on Windows, falling back to the http endpoint.");
}

// note there is no UseUrls call on the named pipe path, and ConfigureNamedPipeEndpoint additionally
// discards any http endpoint declared under Kestrel:Endpoints. The platform check is repeated here
// so the analyzer can see the windows only call is guarded.
if (namedPipeSelected && OperatingSystem.IsWindows())
{
    var namedPipeName = ServiceEndpointHosting.ConfigureNamedPipeEndpoint(builder);

    endpointLog.Add($"Service transport is the local named pipe \\\\.\\pipe\\{namedPipeName}. The http endpoint is not published.");
}
else if (serviceConfig.Host != null && serviceConfig.Port != 0)
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

foreach (var msg in endpointLog)
{
    startup.Log(msg);
}

await startup.ConfigureServices(builder.Services);

var app = builder.Build();

app.MapDefaultEndpoints();

startup.Configure(app, builder.Environment);

app.Start();

startup.Log($"Core service started {string.Join(";", app.Urls)}");

app.WaitForShutdown();

/// <summary>
/// Declare program as partial for reference in tests: https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-8.0
/// </summary>
public partial class Program { }

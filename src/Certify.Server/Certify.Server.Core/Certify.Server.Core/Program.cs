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
#if DEBUG
    //  https://learn.microsoft.com/en-us/aspnet/core/grpc/interprocess-namedpipes?view=aspnetcore-8.0
    builder.WebHost.ConfigureKestrel(opts => opts.ListenNamedPipe("certify-service"));
#endif

    builder.Services.AddWindowsService()
                    .AddHostedService<AgentBackgroundService>();
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

app.Start();

startup.Log($"Core service started {string.Join(";", app.Urls)}");

app.WaitForShutdown();

/// <summary>
/// Declare program as partial for reference in tests: https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-8.0
/// </summary>
public partial class Program { }

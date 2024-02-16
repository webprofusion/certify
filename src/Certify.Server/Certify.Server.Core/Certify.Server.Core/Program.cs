
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var startup = new Certify.Server.Core.Startup(builder.Configuration);

startup.ConfigureServices(builder.Services);

var app = builder.Build();

app.MapDefaultEndpoints();

startup.Configure(app, builder.Environment);

app.Run();

/// <summary>
/// Declare program as partial for reference in tests: https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-8.0
/// </summary>
public partial class Program { }

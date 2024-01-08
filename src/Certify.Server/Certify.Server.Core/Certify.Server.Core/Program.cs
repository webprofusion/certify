
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var startup = new Certify.Server.Core.Startup(builder.Configuration);

startup.ConfigureServices(builder.Services);

var app = builder.Build();

startup.Configure(app, builder.Environment);

app.Run();


using Certify.Server.API;

var builder = WebApplication.CreateBuilder(args);

#if ASPIRE
    builder.AddServiceDefaults();
#endif

var startup = new Startup(builder.Configuration);

startup.ConfigureServices(builder.Services);

var app = builder.Build();

#if ASPIRE
    app.MapDefaultEndpoints();
#endif

startup.Configure(app, builder.Environment);

app.Run();

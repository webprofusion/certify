var builder = DistributedApplication.CreateBuilder(args);

var useIndependentServices = false;

if (Environment.GetEnvironmentVariable("ASPIRE_USE_INDEPENDENT_SERVICES") != null)
{
    useIndependentServices = Environment.GetEnvironmentVariable("ASPIRE_USE_INDEPENDENT_SERVICES").ToLower() == "true";
}

if (useIndependentServices)
{
    var serverCore = builder.AddProject<Projects.Certify_Server_Core>("certifyservercore", "Certify.Server.Core (no service auth)");

    builder.AddProject<Projects.Certify_Server_Hub_Api>("certifyserverhubapi", "Certify.Server.Hub.Api")
        .WithExternalHttpEndpoints()
        .WithReference(serverCore);

}
else
{
    // use combined hubservice
    builder.AddProject<Projects.Certify_Server_HubService>("certify-server-hubservice", "https")
        .WithExternalHttpEndpoints();
}

builder.Build().Run();

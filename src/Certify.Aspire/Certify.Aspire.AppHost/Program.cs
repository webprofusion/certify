var builder = DistributedApplication.CreateBuilder(args);

var useIndependentServices = false;

if (useIndependentServices)
{
    builder.AddProject<Projects.Certify_Server_Hub_Api>("certifyserverhubapi");

    builder.AddProject<Projects.Certify_Server_Core>("certifyservercore");
}
else
{
    // use combined hubservice
    builder.AddProject<Projects.Certify_Server_HubService>("certify-server-hubservice");
}

builder.Build().Run();

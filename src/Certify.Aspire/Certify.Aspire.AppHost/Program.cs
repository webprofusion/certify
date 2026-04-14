var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Certify_Server_HubService>("certify-server-hubservice", "https")
    .WithExternalHttpEndpoints();

builder.Build().Run();

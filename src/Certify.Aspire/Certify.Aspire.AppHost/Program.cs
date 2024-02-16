var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Certify_Server_Api_Public>("certify.server.api.public");

builder.AddProject<Projects.Certify_Server_Core>("certify.server.core");

builder.Build().Run();

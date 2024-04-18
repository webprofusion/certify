var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Certify_Server_Api_Public>("certifyserverapi");

builder.AddProject<Projects.Certify_Server_Core>("certifyservercore");

builder.Build().Run();

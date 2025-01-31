var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Certify_Server_Hub_Api>("certifyserverhubapi");

builder.AddProject<Projects.Certify_Server_Core>("certifyservercore");

builder.Build().Run();

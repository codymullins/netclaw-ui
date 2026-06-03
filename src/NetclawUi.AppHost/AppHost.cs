var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.Netclaw_Web>("web");

builder.Build().Run();

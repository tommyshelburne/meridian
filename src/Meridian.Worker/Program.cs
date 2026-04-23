using Meridian.Infrastructure;
using Meridian.Worker;
using Meridian.Worker.Jobs;

var builder = Host.CreateApplicationBuilder(args);

// Infrastructure: DB, repositories, scoring
var connectionString = builder.Configuration.GetConnectionString("Meridian")
    ?? throw new InvalidOperationException("ConnectionStrings:Meridian is required");
builder.Services.AddMeridianInfrastructure(connectionString, builder.Configuration);

// Worker jobs
builder.Services.AddSingleton<IMeridianJob, IngestionJob>();
builder.Services.AddSingleton<IMeridianJob, SequenceJob>();
builder.Services.AddSingleton<IMeridianJob, BidMonitorJob>();

// Hosted service
builder.Services.AddHostedService<MeridianWorker>();

var host = builder.Build();
host.Run();

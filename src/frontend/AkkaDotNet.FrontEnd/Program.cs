using Akka.Hosting;
using AkkaDotNet.Infrastructure;
using AkkaDotNet.Infrastructure.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();

var akkaConfiguration = builder.Configuration.GetRequiredSection(nameof(StressOptions)).Get<StressOptions>();

builder.Services.AddAkka(ActorSystemConstants.ActorSystemName, configurationBuilder =>
{
    configurationBuilder.WithClusterBootstrap(akkaConfiguration.AkkaClusterOptions,
        new[] { ActorSystemConstants.FrontendRole, ActorSystemConstants.DistributedPubSubRole });
    configurationBuilder.WithReadyCheckActors();
});

var app = builder.Build();

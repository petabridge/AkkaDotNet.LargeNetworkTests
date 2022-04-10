﻿using Akka.Actor;
using Akka.Hosting;
using AkkaDotNet.Infrastructure;
using AkkaDotNet.Infrastructure.Actors;
using AkkaDotNet.Infrastructure.Configuration;
using AkkaDotNet.Infrastructure.Logging;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();

var akkaConfiguration = builder.Configuration.GetRequiredSection(nameof(StressOptions)).Get<StressOptions>();

builder.Services.AddAkka(ActorSystemConstants.ActorSystemName, configurationBuilder =>
{
    configurationBuilder.WithClusterBootstrap(akkaConfiguration.AkkaClusterOptions,
        new[] { ActorSystemConstants.FrontendRole, ActorSystemConstants.DistributedPubSubRole });
    configurationBuilder.WithSerilog(akkaConfiguration.SerilogOptions);
    configurationBuilder.WithReadyCheckActors();
});

var app = builder.Build();

app.MapGet("/ready", async (ActorRegistry registry) =>
{
    var readyCheck = registry.Get<ReadyCheckActor>();
    var checkResult = await readyCheck.Ask<ReadyResult>(ReadyCheck.Instance, TimeSpan.FromSeconds(3));
    if (checkResult.IsReady)
        return Results.StatusCode(200);
    return Results.StatusCode(500);
});

await app.RunAsync();
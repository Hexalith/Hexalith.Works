#!/usr/bin/env dotnet
#:sdk Aspire.AppHost.Sdk@13.6.3
#:property ManagePackageVersionsCentrally=false
#:package Aspire.Hosting.Docker@13.6.3
#:package Aspire.Hosting.Kubernetes@13.6.3-preview.1.26521.2
#:package Aspire.Hosting.Redis@13.6.3
#:package Aspire.Hosting.Keycloak@13.6.3-preview.1.26521.2
#:package CommunityToolkit.Aspire.Hosting.Dapr@13.6.1-beta.935
#:package Hexalith.EventStore.Aspire@3.127.0
#:package Hexalith.EventStore.ServiceDefaults@3.127.0
#:project src/Hexalith.Platform.Composition/Hexalith.Platform.Composition.csproj
using Aspire.Hosting;
using Hexalith.Platform.Composition;

var builder = DistributedApplication.CreateBuilder(args);
PlatformAppModel.ComposeEntry(builder);
builder.Build().Run();

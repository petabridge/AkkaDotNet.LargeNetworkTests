FROM mcr.microsoft.com/dotnet/sdk:6.0 AS base
WORKDIR /app

# important environment variables
ENV StressOptions__AkkaClusterOptions__ManagementPort "9221"
ENV StressOptions__AkkaClusterOptions__ManagementPort "9228"
ENV StressOptions__AkkaClusterOptions__Hostname ""

# 9110 - Petabridge.Cmd
# 9221 - Akka.Cluster
# 9228 - Akka.Management
# 80 - HTTP
EXPOSE 9110 9221 9228 80

# Install Petabridge.Cmd client so it can be invoked remotely via
# Docker or K8s 'exec` commands
RUN dotnet tool install --global pbm 

# RUN pbm help

COPY ./bin/Release/net6.0/publish/ /app

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS app
WORKDIR /app

COPY --from=base /app /app

# copy .NET Core global tool
COPY --from=base /root/.dotnet /root/.dotnet/

# Needed because https://stackoverflow.com/questions/51977474/install-dotnet-core-tool-dockerfile
ENV PATH="${PATH}:/root/.dotnet/tools"

CMD ["dotnet", "AkkaDotNet.FrontEnd.dll"]
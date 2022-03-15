FROM mcr.microsoft.com/dotnet/runtime:6.0-bullseye-slim-amd64 AS base
RUN apt-get update
RUN DEBIAN_FRONTEND=noninteractive apt-get install -y libopus0 libopus-dev libsodium23 libsodium-dev
RUN apt-get clean
RUN rm -rf /var/lib/apt/lists/*
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["JeBolleMams.csproj", "./"]
RUN dotnet restore "JeBolleMams.csproj"
COPY . .
WORKDIR "/src/"
RUN dotnet build "JeBolleMams.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "JeBolleMams.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "JeBolleMams.dll"]

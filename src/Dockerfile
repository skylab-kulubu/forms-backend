# 1) Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# 2) Build image
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore "Skylab.sln"

WORKDIR "/src/API/Skylab.Api"
RUN dotnet publish "Skylab.Api.csproj" -c Release -o /app/publish

# 3) Final image
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Skylab.Api.dll"]
# 1) Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# 2) Build image
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["Forms.Api/Forms.Api.csproj", "Forms.Api/"]
RUN dotnet restore "Forms.Api/Forms.Api.csproj"

COPY . .
WORKDIR "/src/Forms.Api"
RUN dotnet publish "Forms.Api.csproj" -c Release -o /app/publish

# 3) Final image
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Forms.Api.dll"]
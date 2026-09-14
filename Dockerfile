# Multi-stage build: SDK image builds/publishes, slim ASP.NET runtime image runs.
# linux-x64 explicitly so Google.OrTools' native solver library is the correct one
# for the container's actual OS/arch, not whatever the build host happens to be.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY MakroChef.slnx ./
COPY src/MakroChef.Domain/MakroChef.Domain.csproj src/MakroChef.Domain/
COPY src/MakroChef.Mcp/MakroChef.Mcp.csproj src/MakroChef.Mcp/
COPY src/MakroChef.Nutrition/MakroChef.Nutrition.csproj src/MakroChef.Nutrition/
COPY src/MakroChef.Solver/MakroChef.Solver.csproj src/MakroChef.Solver/
COPY src/MakroChef.Data/MakroChef.Data.csproj src/MakroChef.Data/
COPY src/MakroChef.Agent/MakroChef.Agent.csproj src/MakroChef.Agent/
COPY src/MakroChef.Api/MakroChef.Api.csproj src/MakroChef.Api/
RUN dotnet restore src/MakroChef.Api/MakroChef.Api.csproj -r linux-x64

COPY src/ src/
COPY web/ web/
RUN dotnet publish src/MakroChef.Api/MakroChef.Api.csproj \
    -c Release -r linux-x64 --self-contained false \
    -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
COPY web/ web/

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "MakroChef.Api.dll"]

# Use the official ASP.NET Core runtime as base image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443
# Use the SDK image to build the app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# Copy the project file
COPY ["EinAutomation.Api.csproj", "./"]
RUN dotnet restore "EinAutomation.Api.csproj"
# Copy the rest of the source code
COPY . .
WORKDIR "/src"
RUN dotnet build "EinAutomation.Api.csproj" -c Release -o /app/build
# Publish the application
FROM build AS publish
RUN dotnet publish "EinAutomation.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false
# Final stage - create the runtime image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EinAutomation.Api.dll"]

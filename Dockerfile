# Base image with ASP.NET runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Use SDK image to build the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["EinAutomation.Api.csproj", "./"]
RUN dotnet restore "EinAutomation.Api.csproj"
COPY . .
WORKDIR "/src"
RUN dotnet build "EinAutomation.Api.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "EinAutomation.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final image with everything needed to run the app
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Install Chromium and Selenium dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    libglib2.0-0 \
    chromium \
    chromium-driver \
    libnss3 \
    libx11-xcb1 \
    libxcb1 \
    libxcomposite1 \
    libxcursor1 \
    libxdamage1 \
    libxext6 \
    libxi6 \
    libxrandr2 \
    libxss1 \
    libxtst6 \
    libgbm1 \
    libgconf-2-4 \
    libfontconfig1 \
    libxshmfence1 \
    libasound2 \
    fonts-liberation \
    fonts-freefont-ttf \
    libjpeg62-turbo \
    libopenjp2-7 \
    libfreetype6 \
    xdg-utils \
    && rm -rf /var/lib/apt/lists/*

# Set environment variables for Chromium and Chromedriver
ENV CHROME_BIN=/usr/bin/chromium \
    CHROMEDRIVER_PATH=/usr/bin/chromedriver \
    AZURE_STORAGE_CONNECTION_STRING=""

# Copy published output from build stage
COPY --from=publish /app/publish .

# Create non-root user and set ownership
RUN useradd -m appuser && chown -R appuser /app
USER appuser

# Start the application
ENTRYPOINT ["dotnet", "EinAutomation.Api.dll"]

# Use a Linux-based image with .NET 9.0 SDK
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-env

# Set the working directory
WORKDIR /app

# Copy the project files into the container, excluding ignored files
COPY . . 

# Restore, clean, and build the project
RUN dotnet build 'Dotnet/VRCX-Electron.csproj' \
    -p:Configuration=Release \
    -p:Platform=x64 \
    -p:RestorePackagesConfig=true \
    -t:"Restore;Clean;Build" \
    -m --self-contained

# Install Node 24 from NodeSource
RUN curl -fsSL https://deb.nodesource.com/setup_24.x | bash - \
    && apt-get install -y nodejs

# Install Node dependencies
RUN ELECTRON_SKIP_BINARY_DOWNLOAD=1 npm ci

# Build the frontend
RUN npm run prod-web

# Set environment variable
ENV VRCX_PORT=3333

# START BACKEND
ENTRYPOINT ["node", "./src-backend/index.js"]

# Expose the port
EXPOSE ${VRCX_PORT}

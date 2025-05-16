# Use a Linux-based image with .NET 9.0 SDK
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-env

# Set the working directory
WORKDIR /app

# Copy the project files into the container, excluding ignored files
COPY . . 

# Restore, clean, and build the project
RUN dotnet build 'Server/Server.csproj' \
    -p:Configuration=Release \
    -p:Platform=x64 \
    -p:RestorePackagesConfig=true \
    -t:"Restore;Clean;Build" \
    -m    

# Install Node 24 from NodeSource
RUN curl -fsSL https://deb.nodesource.com/setup_24.x | bash - \
    && apt-get install -y nodejs

# Install Node dependencies
RUN ELECTRON_SKIP_BINARY_DOWNLOAD=1 npm ci

# Build the frontend
RUN npm run prod-web

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS prod-env

# Set the working directory
WORKDIR /app

# Copy the build artifacts from the previous stage
COPY --from=build-env /app/build/html ./build/html
COPY --from=build-env /app/Server ./Server
COPY --from=build-env /app/VRCX.ico ./VRCX.ico

# Set environment variables
ENV VRCX_PORT=3333
ENV VRCX_PASSWORD=""
ENV VRCX_ROOT_DIR=/app

# START BACKEND
ENTRYPOINT ["./Server/bin/x64/Release/net9.0/Server"]

# Expose the port
EXPOSE ${VRCX_PORT}

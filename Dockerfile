# === Build Stage ===
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY /ProbuildBackend/ ./
RUN dotnet restore
RUN dotnet publish -c Release -o out

# === Runtime Stage ===
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install required native dependencies
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        libgdiplus \
        libc6-dev \
        libx11-dev \
        libxext6 \
        libxrender1 && \
    rm -rf /var/lib/apt/lists/* && \
    ln -s /usr/lib/libgdiplus.so /usr/lib/libgdiplus.so.0 || true

# Set environment for Pdfium native libs
ENV LD_LIBRARY_PATH=/app/runtimes/linux-x64/native

COPY --from=build /app/out ./

EXPOSE 443
ENTRYPOINT ["dotnet", "ProbuildBackend.dll"]

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
ENV LD_LIBRARY_PATH=/app/runtimes/linux-x64/native:$LD_LIBRARY_PATH

# Copy the published output
COPY --from=build /app/out ./
# Explicitly copy the runtimes folder to ensure libpdfium.so is included
COPY --from=build /app/ProbuildBackend/runtimes/ ./runtimes/

EXPOSE 443
ENTRYPOINT ["dotnet", "ProbuildBackend.dll"]
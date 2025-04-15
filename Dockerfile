# === Build Stage ===
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app
COPY /ProbuildBackend/ ./
RUN dotnet restore
RUN dotnet publish -c Release -o out

# === Runtime Stage ===
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install dependencies for PdfiumViewer (and System.Drawing.Common)
RUN apt-get update && \
    apt-get install -y \
        libgdiplus \
        libc6-dev \
        libx11-dev \
        libxext6 \
        libxrender1 && \
    ln -s libgdiplus.so /usr/lib/libgdiplus.so

# Set Pdfium native lib path
ENV LD_LIBRARY_PATH=/app/runtimes/linux-x64/native

COPY --from=build /app/out ./

EXPOSE 443
ENTRYPOINT ["dotnet", "ProbuildBackend.dll"]

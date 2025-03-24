using Elastic.Apm.NetCoreAll;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Middleware;
using ProbuildBackend.Models;
using ProbuildBackend.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks; // For health checks
using Microsoft.Data.SqlClient; // For custom SQL Server health check
using Azure.Storage.Blobs; // For custom Azure Blob Storage health check

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Configure CORS to allow Angular app with credentials
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp", policy =>
    {
        policy.WithOrigins(
            "http://localhost:4200", // For local development
            "https://probuildai-ui.wonderfulgrass-0f331a8e.centralus.azurecontainerapps.io" // For production
        )
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials(); // Required for SignalR with credentials
    });
});

// Add services to the container
builder.Services.AddControllers(options =>
{
    options.Filters.Add(new RequestSizeLimitAttribute(200 * 1024 * 1024)); // 200MB
})
.ConfigureApiBehaviorOptions(options =>
{
    options.SuppressModelStateInvalidFilter = true;
});

// Configure FormOptions for multipart requests
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 200 * 1024 * 1024; // 200MB
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartBoundaryLengthLimit = int.MaxValue;
});

// Kestrel configuration
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 200 * 1024 * 1024; // 200MB
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(5); // 5-minute timeout
});

var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
                       ?? builder.Configuration.GetConnectionString("DefaultConnection");

// Add health checks for database and Blob Storage
builder.Services.AddHealthChecks()
    .AddCheck("SQLServer", () =>
    {
        try
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand("SELECT 1;", connection))
                {
                    command.ExecuteScalar();
                }
                return HealthCheckResult.Healthy("SQL Server is healthy");
            }
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"SQL Server is unhealthy: {ex.Message}");
        }
    })
    .AddCheck("AzureBlobStorage", () =>
    {
        try
        {
            var blobConnectionString = builder.Configuration.GetConnectionString("AzureBlobConnection");
            var blobServiceClient = new BlobServiceClient(blobConnectionString);
            blobServiceClient.GetAccountInfo();
            return HealthCheckResult.Healthy("Azure Blob Storage is healthy");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Azure Blob Storage is unhealthy: {ex.Message}");
        }
    });

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddIdentity<UserModel, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.AddTransient<IEmailSender, EmailSender>();
builder.Services.AddSingleton<AzureBlobService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<WebSocketManager>();
builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor(); // Required for AzureBlobService

var app = builder.Build();

// Log the URLs the application is listening on
var listeningUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "Not set";
app.Logger.LogInformation("Application is listening on: {Urls}", listeningUrls);

// Add health endpoint before other middleware to ensure it's accessible
app.MapGet("/health", () => Results.Ok("Healthy"));
app.MapHealthChecks("/health/details");

// Map SignalR hub
app.MapHub<ProgressHub>("/progressHub");

// Middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

var elasticEnabledString = Environment.GetEnvironmentVariable("ELASTIC_ENABLED");
if (string.IsNullOrEmpty(elasticEnabledString))
{
    Console.WriteLine("Warning: ELASTIC_ENABLED environment variable is not set. Defaulting to false.");
    elasticEnabledString = "false";
}
var elasticEnabled = bool.Parse(elasticEnabledString);
if (elasticEnabled)
{
    app.UseAllElasticApm(builder.Configuration);
}

app.UseWebSockets();
app.UseRouting();
app.UseCors("AllowAngularApp"); // Apply the named CORS policy
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Wrap the application startup in a try-catch to log any errors
try
{
    app.Logger.LogInformation("Application starting...");

    // Test database connection
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        dbContext.Database.EnsureCreated();
        app.Logger.LogInformation("Successfully connected to the database");
    }

    // Test Blob Storage connection
    var blobService = app.Services.GetRequiredService<AzureBlobService>();
    app.Logger.LogInformation("Successfully initialized Azure Blob Service");

    app.Run();
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Application failed to start");
    throw;
}
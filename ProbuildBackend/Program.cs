﻿using ProbuildBackend.Interface;
using ProbuildBackend.Services;
using Elastic.Apm.NetCoreAll;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ProbuildBackend.Middleware;
using ProbuildBackend.Models;
using ProbuildBackend.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using ProbuildBackend.Infrastructure;
using IEmailSender = ProbuildBackend.Interface.IEmailSender;

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
            "http://localhost:4200",
            "https://probuildai-ui.wonderfulgrass-0f331ae8.centralus.azurecontainerapps.io",
            "https://app.probuildai.com",
            "https://qa-probuildai-ui.wonderfulgrass-0f331ae8.centralus.azurecontainerapps.io"
        )
        .AllowAnyHeader()
        .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
        .AllowCredentials();
    });
});

// Configure the token provider for password reset
builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
{
    options.TokenLifespan = TimeSpan.FromHours(24); // Set expiration to 24 hours
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
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
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

builder.Services.AddDataProtection()
    .SetApplicationName("ProbuildAI")
    .PersistKeysToDbContext<ApplicationDbContext>()
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90)) // Optional: lengthen key lifetime
    .UseCryptographicAlgorithms(new AuthenticatedEncryptorConfiguration
    {
        EncryptionAlgorithm = EncryptionAlgorithm.AES_256_CBC,
        ValidationAlgorithm = ValidationAlgorithm.HMACSHA256
    });

#if(DEBUG)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var azureBlobStorage = builder.Configuration.GetConnectionString("AzureBlobConnection");
var signalrConn =
    builder.Configuration["Azure:SignalR:ConnectionString"];
#else
var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
var azureBlobStorage = Environment.GetEnvironmentVariable("AZURE_BLOB_KEY");
var signalrConn = Environment.GetEnvironmentVariable("AzureSignalRConnectionString");
#endif
var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY") ?? builder.Configuration["Jwt:Key"];



var configuration = builder.Configuration;
// Configure DbContext with retry policy to handle rate-limiting
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        connectionString,
        sqlServerOptions => sqlServerOptions
            .UseNetTopologySuite()
            .EnableRetryOnFailure(
                maxRetryCount: 3, // Reduced number of retries
                maxRetryDelay: TimeSpan.FromSeconds(5), // Increased delay between retries
                errorNumbersToAdd: null
            )
    ));

builder.Services.AddScoped<ContractService>();
builder.Services.AddIdentity<UserModel, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedEmail = true;
    options.Tokens.PasswordResetTokenProvider = TokenOptions.DefaultProvider;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
{
    options.TokenLifespan = TimeSpan.FromHours(24);
});
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;

            // If the request is for our hub...
            if (!string.IsNullOrEmpty(accessToken) &&
                (path.StartsWithSegments("/chathub") ||
                 path.StartsWithSegments("/hubs/progressHub") ||
                 path.StartsWithSegments("/hubs/notifications")))
            {
                // Read the token out of the query string
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});


builder.Services.AddScoped<IDocumentProcessorService, DocumentProcessorService>();
builder.Services.AddTransient<IEmailSender, EmailSender>();
builder.Services.AddScoped<IEmailTemplateService, EmailTemplateService>();
builder.Services.AddSingleton<AzureBlobService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<SubscriptionService>();
builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, UserIdFromClaimProvider>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<WebSocketManager>();
var signalR = builder.Services.AddSignalR();
if (!string.IsNullOrWhiteSpace(signalrConn))
{
    signalR.AddAzureSignalR(o =>
    {
        o.ConnectionString = signalrConn;
        o.InitialHubServerConnectionCount = 1;
        o.MaxHubServerConnectionCount = 1; // leaves room for clients
    });
}
builder.Services.AddLogging(configure => configure.AddConsole());
builder.Services.AddHttpContextAccessor(); // Required for AzureBlobService
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString)); // Replace with UseSqlServerStorage in production
builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = 2; // or even 1 if Gemini calls are large
});
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IEmailSender, EmailSender>();
builder.Services.AddScoped<IConversationRepository, SqlConversationRepository>();
builder.Services.AddScoped<IPromptManagerService, PromptManagerService>();
// The DI container will automatically inject the other services into GeminiAiService's constructor
builder.Services.AddScoped<IAiService, GeminiAiService>();
builder.Services.AddScoped<IAiAnalysisService, AiAnalysisService>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddTransient<IKeepAliveService, KeepAliveService>();
builder.Services.AddScoped<IPdfImageConverter, PdfImageConverter>();
builder.Services.AddScoped<IPdfTextExtractionService, PdfTextExtractionService>();
builder.Services.Configure<OcrSettings>(configuration.GetSection("OcrSettings"));
builder.Services.AddScoped(sp => sp.GetRequiredService<IOptions<OcrSettings>>().Value);
builder.Services.AddScoped<UserModerationService>();
builder.Services.AddScoped<IPdfConversionService, PdfConversionService>();

builder.Services.AddHostedService<TokenCleanupService>();
builder.Services.AddHangfireServer();
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{

    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // Ensure database is created
    context.Database.EnsureCreated();

    // Initialize data protection keys if none exist
    var keyManager = scope.ServiceProvider.GetRequiredService<IKeyManager>();
    var keys = keyManager.GetAllKeys();

    if (!keys.Any())
    {
        app.Logger.LogInformation("No data protection keys found. Creating new key...");
        // This will trigger key creation
        var dataProtector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Microsoft.AspNetCore.Identity.UserManager<UserModel>");
        var testData = dataProtector.Protect("test");
        app.Logger.LogInformation("Data protection key created successfully");
    }
    else
    {
        app.Logger.LogInformation($"Found {keys.Count()} data protection key(s)");
    }
}

// Map a simple health endpoint
app.MapGet("/health", () => Results.Ok("Healthy"));

// Map SignalR hub
app.MapHub<ProgressHub>("/hubs/progressHub");
app.Logger.LogInformation("ProgressHub endpoint mapped at /hubs/progressHub");
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHub<ChatHub>("/chathub");
app.Logger.LogInformation("ChatHub endpoint mapped at /chathub");

// Log the URLs the application is listening on
var listeningUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "Not set";
app.Logger.LogInformation("Application is listening on: {Urls}", listeningUrls);

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

// Bypass HTTPS redirection for /health endpoint to ensure compatibility
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/health"), appBuilder =>
{
    appBuilder.UseHttpsRedirection();
});

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
app.UseCors("AllowAngularApp"); // Apply the named CORS policy after health endpoint
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Wrap the application startup in a try-catch to log any errors with retry logic
try
{
    app.Logger.LogInformation("Application starting...");

    // Test database connection with retry logic and exponential backoff
    app.Logger.LogInformation("Attempting to connect to the database...");
    bool connected = false;
    int maxRetries = 3;
    int retryDelaySeconds = 5;
    for (int i = 0; i < maxRetries && !connected; i++)
    {
        try
        {
            using (var scope = app.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                app.Logger.LogInformation("DbContext created. Calling EnsureCreated...");
                dbContext.Database.EnsureCreated();
                app.Logger.LogInformation("EnsureCreated completed successfully");
                app.Logger.LogInformation("Successfully connected to the database");
                connected = true;
            }
        }
        catch (Exception ex)
        {
            app.Logger.LogWarning(ex, "Failed to connect to the database on attempt {Attempt}. Retrying in {Delay} seconds...", i + 1, retryDelaySeconds);
            if (i == maxRetries - 1)
            {
                app.Logger.LogError(ex, "Failed to connect to the database after {MaxRetries} attempts", maxRetries);
                throw;
            }
            await Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds));
            retryDelaySeconds *= 2; // Exponential backoff
        }
    }

    // Test Blob Storage connection
    app.Logger.LogInformation("Attempting to initialize Azure Blob Service...");
    var blobService = app.Services.GetRequiredService<AzureBlobService>();
    app.Logger.LogInformation("Successfully initialized Azure Blob Service");


    using (var scope = app.Services.CreateScope())
    {
        var keyManager = scope.ServiceProvider.GetRequiredService<IKeyManager>();
        var keys = keyManager.GetAllKeys();

        app.Logger.LogInformation("🔐 Data Protection Keys loaded at startup:");
        foreach (var key in keys)
        {
            app.Logger.LogInformation($"🔑 KeyId: {key.KeyId} | Created: {key.CreationDate} | Expires: {key.ExpirationDate}");
        }

        var keyXmls = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().DataProtectionKeys.ToList();
        foreach (var keyRow in keyXmls)
        {
            app.Logger.LogInformation($"🧱 DB Row KeyId: {keyRow.FriendlyName}");
        }
    }

    app.Logger.LogInformation("Application startup completed successfully. Starting to run...");
    app.Run();
}
catch (Exception ex)
{
    app.Logger.LogError(ex, "Application failed to start. Exception: {Message}", ex.Message);
    app.Logger.LogError(ex, "Stack trace: {StackTrace}", ex.StackTrace);
    throw;
}


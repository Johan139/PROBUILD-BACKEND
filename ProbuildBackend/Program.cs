using Elastic.Apm.NetCoreAll;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProbuildBackend.Interface;
using ProbuildBackend.Middleware;
using ProbuildBackend.Models;
using ProbuildBackend.Options;
using ProbuildBackend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using MongoDB.Driver.Core.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Load configuration and expand env variables
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

foreach (var (key, value) in builder.Configuration.AsEnumerable())
{
    if (value != null && value.Contains("${"))
    {
        builder.Configuration[key] = Environment.ExpandEnvironmentVariables(value);
    }
}

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
            "https://app.probuildai.com/"
        )
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
    });
});

builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
{
    options.TokenLifespan = TimeSpan.FromHours(24);
});

builder.Services.AddControllers(options =>
{
    options.Filters.Add(new RequestSizeLimitAttribute(200 * 1024 * 1024));
})
.ConfigureApiBehaviorOptions(options =>
{
    options.SuppressModelStateInvalidFilter = true;
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 200 * 1024 * 1024;
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartBoundaryLengthLimit = int.MaxValue;
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 200 * 1024 * 1024;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(5);
});

builder.Services.AddDataProtection()
    .SetApplicationName("ProbuildAI")
    .PersistKeysToDbContext<ApplicationDbContext>()
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90))
    .UseCryptographicAlgorithms(new AuthenticatedEncryptorConfiguration
    {
        EncryptionAlgorithm = EncryptionAlgorithm.AES_256_CBC,
        ValidationAlgorithm = ValidationAlgorithm.HMACSHA256
    });

var connectionString =
    Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
    ?? builder.Configuration.GetConnectionString("DefaultConnection");

var azureBlobStorage = builder.Configuration.GetConnectionString("AzureBlobConnection");
var jwtKey = builder.Configuration["Jwt:Key"];

var configuration = builder.Configuration;
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        connectionString,
        sqlServerOptions => sqlServerOptions.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null)));

builder.Services.AddIdentity<UserModel, IdentityRole>(options =>
{
    options.SignIn.RequireConfirmedEmail = true;
    options.Tokens.PasswordResetTokenProvider = TokenOptions.DefaultProvider;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

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
});

builder.Services.AddScoped<DocumentProcessorService>();
builder.Services.AddTransient<IEmailSender, EmailSender>();
builder.Services.AddSingleton<AzureBlobService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpClient();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<WebSocketManager>();
builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString));
builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = 2;
});
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IEmailSender, EmailSender>();
builder.Services.AddScoped<IConversationRepository, SqlConversationRepository>();
builder.Services.AddScoped<IPromptManagerService, PromptManagerService>();
builder.Services.AddScoped<IAiService, GeminiAiService>();
builder.Services.AddScoped<IProjectAnalysisOrchestrator, ProjectAnalysisOrchestrator>();
builder.Services.AddScoped<IComprehensiveAnalysisService, ComprehensiveAnalysisService>();
builder.Services.AddScoped<IPdfImageConverter, PdfImageConverter>();
builder.Services.Configure<OcrSettings>(configuration.GetSection("OcrSettings"));
builder.Services.AddScoped(sp => sp.GetRequiredService<IOptions<OcrSettings>>().Value);
builder.Services.AddHostedService<TokenCleanupService>();
builder.Services.AddHangfireServer();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    context.Database.EnsureCreated();

    var keyManager = scope.ServiceProvider.GetRequiredService<IKeyManager>();
    var keys = keyManager.GetAllKeys();

    if (!keys.Any())
    {
        app.Logger.LogInformation("No data protection keys found. Creating new key...");
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

app.MapGet("/health", () => Results.Ok("Healthy"));
app.MapHub<ProgressHub>("/progressHub");
app.MapHub<NotificationHub>("/hubs/notifications");

var listeningUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "Not set";
app.Logger.LogInformation("Application is listening on: {Urls}", listeningUrls);

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

app.UseWhen(context => !context.Request.Path.StartsWithSegments("/health"), appBuilder =>
{
    appBuilder.UseHttpsRedirection();
});

app.UseStaticFiles();

var elasticEnabledString = Environment.GetEnvironmentVariable("ELASTIC_ENABLED") ?? "false";
if (bool.TryParse(elasticEnabledString, out var elasticEnabled) && elasticEnabled)
{
    app.UseAllElasticApm(builder.Configuration);
}

app.UseWebSockets();
app.UseRouting();
app.UseCors("AllowAngularApp");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

try
{
    app.Logger.LogInformation("Application starting...");
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
            retryDelaySeconds *= 2;
        }
    }

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

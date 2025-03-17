using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ProbuildBackend.Models;
using Microsoft.AspNetCore.Identity.UI.Services;
using ProbuildBackend.Services;
using Elastic.Apm.NetCoreAll;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddCors(policyBuilder =>
    policyBuilder.AddDefaultPolicy(policy =>
        policy.WithOrigins("*").AllowAnyHeader().AllowAnyMethod())
);

// Add services to the container.
builder.Services.AddControllers();

var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
                       ?? builder.Configuration.GetConnectionString("DefaultConnection");

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

var app = builder.Build();

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

app.UseHttpsRedirection();  // Ensure this is here

app.UseStaticFiles();

// Enable Elastic APM Middleware

var elasticEnabledString = Environment.GetEnvironmentVariable("ELASTIC_ENABLED");
if (string.IsNullOrEmpty(elasticEnabledString))
{
    Console.WriteLine("Warning: ELASTIC_ENABLED environment variable is not set. Defaulting to false.");
    elasticEnabledString = "false";
}
var elasticEnabled = bool.Parse(elasticEnabledString);
if (elasticEnabled)
{
#pragma warning disable CS0618 // Type or member is obsolete
    app.UseAllElasticApm(builder.Configuration);
#pragma warning restore CS0618 // Type or member is obsolete
}



app.UseWebSockets();

app.Use(async (context, next) =>
{
    if (context.Request.Path == "/ws")
    {
        if (context.WebSockets.IsWebSocketRequest)
        {
            var userId = context.Request.Query["userId"];
            var webSocket = await context.WebSockets.AcceptWebSocketAsync();
            var webSocketManager = context.RequestServices.GetRequiredService<WebSocketManager>();
            await webSocketManager.HandleWebSocketAsync(webSocket, userId);
        }
        else
        {
            context.Response.StatusCode = 400;
        }
    }
    else
    {
        await next();
    }
});

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseCors();
app.MapControllers();

app.Run();


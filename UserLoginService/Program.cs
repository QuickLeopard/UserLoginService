using Microsoft.EntityFrameworkCore;
using UserLoginService.Data;
using UserLoginService.Services;
using System.Threading;
using StackExchange.Redis;

// Optimize thread pool for high concurrency
ThreadPool.SetMinThreads(200, 200);

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel with improved settings for high load
builder.WebHost.ConfigureKestrel(options =>
{
    // Increase timeouts
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(1);
    options.Limits.MaxConcurrentConnections = 1000;
    options.Limits.MaxConcurrentUpgradedConnections = 1000;
});

// Add services to the container.
builder.Services.AddGrpc(options =>
{
    options.MaxReceiveMessageSize = 16 * 1024 * 1024; // 16 MB
    options.MaxSendMessageSize = 16 * 1024 * 1024; // 16 MB
    options.EnableDetailedErrors = true;
});

// Configure PostgreSQL
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        pgOptions => pgOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorCodesToAdd: null)
    )
);

// Configure Redis Connection
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = ConfigurationOptions.Parse(
        builder.Configuration.GetConnectionString("RedisConnection") ?? "redis:6379");
    configuration.ConnectRetry = 5;
    configuration.ConnectTimeout = 5000;
    return ConnectionMultiplexer.Connect(configuration);
});

// Configure Redis Cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("RedisConnection") ?? "redis:6379";
    options.InstanceName = "UserLoginService_";
});

// Register the cache service
builder.Services.AddSingleton<ICacheService, RedisCacheService>();

// Register the Redis Stream service
builder.Services.AddSingleton<IRedisStreamService, RedisStreamService>();

// Register background services
builder.Services.AddHostedService<RedisStreamConsumerService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
app.MapGrpcService<UserLoginServiceImpl>();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();

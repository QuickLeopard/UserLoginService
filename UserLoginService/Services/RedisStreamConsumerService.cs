using Microsoft.Extensions.Hosting;

namespace UserLoginService.Services
{
    public class RedisStreamConsumerService : BackgroundService
    {
        private readonly IRedisStreamService _streamService;
        private readonly ILogger<RedisStreamConsumerService> _logger;

        public RedisStreamConsumerService(
            IRedisStreamService streamService,
            ILogger<RedisStreamConsumerService> logger)
        {
            _streamService = streamService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Redis Stream Consumer Service is starting");

            try
            {
                await _streamService.StartConsumingAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error occurred in Redis Stream Consumer Service");
                
                // Restart the task after a short delay to prevent tight loops in case of recurring errors
                await Task.Delay(5000, stoppingToken);
                
                // Attempt to restart by throwing, which will trigger the service to restart
                throw;
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Redis Stream Consumer Service is stopping");
            await base.StopAsync(stoppingToken);
        }
    }
}

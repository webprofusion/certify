namespace Certify.Server.Core
{
    public class StubBackgroundService : BackgroundService
    {
        public StubBackgroundService(ILoggerFactory loggerFactory)
        {
            Logger = loggerFactory.CreateLogger<StubBackgroundService>();
        }

        public ILogger Logger { get; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Logger.LogInformation("Service is starting.");

            stoppingToken.Register(() => Logger.LogInformation("Service is stopping."));

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }

            Logger.LogInformation("Service has stopped.");
        }
    }
}

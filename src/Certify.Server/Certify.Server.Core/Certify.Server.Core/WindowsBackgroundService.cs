namespace Certify.Server.Core
{

    public class WindowsBackgroundService : BackgroundService
    {
        public WindowsBackgroundService(ILoggerFactory loggerFactory)
        {
            Logger = loggerFactory.CreateLogger<WindowsBackgroundService>();
        }

        public ILogger Logger { get; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Logger.LogInformation("Service is starting.");

            stoppingToken.Register(() => Logger.LogInformation("Service is stopping."));

            while (!stoppingToken.IsCancellationRequested)
            {
                Logger.LogInformation("Service is doing background work.");

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }

            Logger.LogInformation("Service has stopped.");
        }
    }
}

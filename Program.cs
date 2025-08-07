using Serilog;
using TestService.Services;

namespace TestService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
            .WriteTo.File("C:\\Logs\\MyService\\app-log.txt", rollingInterval: RollingInterval.Day)
            .WriteTo.File("C:\\Logs\\MyService\\error-log.txt", restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Error)
            .CreateLogger();

            try
            {
                var builder = Host.CreateApplicationBuilder(args);

                // Add Windows Service support
                builder.Services.AddWindowsService();

                // Clear default logging providers and add Serilog
                builder.Logging.ClearProviders();
                builder.Logging.AddSerilog();

                // Register services
                builder.Services.AddSingleton<ConfigurationService>();
                builder.Services.AddSingleton<INetworkService, NetworkService>();
                builder.Services.AddHostedService<Worker>();

                var host = builder.Build();

                Log.Information("Starting the service...");
                host.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "An unhandled exception occurred during bootstrapping the service.");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}

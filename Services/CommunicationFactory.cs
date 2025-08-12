namespace TestService.Services
{
    public class CommunicationFactory(ILoggerFactory loggerFactory, ConfigurationService configService)
    {
        private readonly ILoggerFactory _loggerFactory = loggerFactory;
        private readonly ConfigurationService _configService = configService;

        public INetworkService CreateCommunicationService()
        {
            var config = _configService.GetConfiguration();

            return config.CommunicationType.ToUpper() switch
            {
                "TCP" => new NetworkService(_loggerFactory.CreateLogger<NetworkService>(), _configService),
                "SERIAL" => new SerialService(_loggerFactory.CreateLogger<SerialService>(), _configService),
                _ => throw new InvalidOperationException($"Unsupported communication type: {config.CommunicationType}")
            };
        }
    }
}
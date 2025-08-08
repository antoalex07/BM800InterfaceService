namespace TestService.Services
{
    public class CommunicationFactory
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ConfigurationService _configService;

        public CommunicationFactory(ILoggerFactory loggerFactory, ConfigurationService configService)
        {
            _loggerFactory = loggerFactory;
            _configService = configService;
        }

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
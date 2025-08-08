using System.Text.Json;
using TestService.Models;

namespace TestService.Services
{
    public class ConfigurationService
    {
        private readonly ILogger<ConfigurationService> _logger;
        private readonly string _configPath = "config.json";
        private ConnectionConfig? _config;
        private FileSystemWatcher? _fileWatcher;
        private readonly object _configLock = new object();

        public event EventHandler<ConnectionConfig>? ConfigurationChanged;

        public ConfigurationService(ILogger<ConfigurationService> logger)
        {
            _logger = logger;
            LoadConfiguration();
            SetupFileWatcher();
        }

        public ConnectionConfig GetConfiguration()
        {
            lock (_configLock)
            {
                return _config ?? new ConnectionConfig();
            }
        }

        private void LoadConfiguration()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    _logger.LogWarning("Configuration file not found at {ConfigPath}. Creating default configuration.", _configPath);
                    CreateDefaultConfiguration();
                    return;
                }

                string jsonContent = File.ReadAllText(_configPath);
                var config = JsonSerializer.Deserialize<ConnectionConfig>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                lock (_configLock)
                {
                    _config = config ?? new ConnectionConfig();
                }

                _logger.LogInformation("Configuration loaded successfully from {ConfigPath}", _configPath);
                _logger.LogInformation("Communication Type: {Type}", _config.CommunicationType);

                if (_config.CommunicationType.ToUpper() == "TCP")
                {
                    _logger.LogInformation("TCP Settings - Method: {Method}, IP: {IP}, Port: {Port}",
                        _config.ConnectionMethod, _config.IpAddress, _config.Port);
                }
                else if (_config.CommunicationType.ToUpper() == "SERIAL")
                {
                    _logger.LogInformation("Serial Settings - Port: {Port}, Baud: {Baud}, Parity: {Parity}, DataBits: {DataBits}, StopBits: {StopBits}",
                        _config.PortName, _config.BaudRate, _config.Parity, _config.DataBits, _config.StopBits);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load configuration. Using default configuration.");
                lock (_configLock)
                {
                    _config = new ConnectionConfig();
                }
            }
        }

        private void CreateDefaultConfiguration()
        {
            var defaultConfig = new ConnectionConfig
            {
                CommunicationType = "SERIAL",
                IpAddress = "127.0.0.1",
                Port = 8080,
                ConnectionMethod = "client",
                PortName = "COM1",
                BaudRate = 9600,
                Parity = "None",
                DataBits = 8,
                StopBits = "One",
                Handshake = "None",
                DtrEnable = false,
                RtsEnable = false,
                ReconnectIntervalSeconds = 30,
                ConnectionTimeoutSeconds = 10,
                ReceiveTimeoutSeconds = 30,
                SendTimeoutSeconds = 10,
                EnableKeepAlive = true,
                KeepAliveIntervalSeconds = 60,
                MaxReconnectAttempts = -1,
                LogLevel = "Information",
                MessageDelimiter = "",
                ReadBufferSize = 4096,
                WriteBufferSize = 2048
            };

            try
            {
                string jsonString = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(_configPath, jsonString);
                lock (_configLock)
                {
                    _config = defaultConfig;
                }
                _logger.LogInformation("Default configuration created at {ConfigPath}", _configPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create default configuration file");
                lock (_configLock)
                {
                    _config = defaultConfig;
                }
            }
        }

        private void SetupFileWatcher()
        {
            try
            {
                _fileWatcher = new FileSystemWatcher
                {
                    Path = Path.GetDirectoryName(Path.GetFullPath(_configPath)) ?? Environment.CurrentDirectory,
                    Filter = Path.GetFileName(_configPath),
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                };

                _fileWatcher.Changed += OnConfigFileChanged;
                _fileWatcher.EnableRaisingEvents = true;

                _logger.LogInformation("Configuration file watcher started");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to setup configuration file watcher");
            }
        }

        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            try
            {
                // Delay to ensure file write is complete
                Thread.Sleep(500);

                _logger.LogInformation("Configuration file changed. Reloading...");
                LoadConfiguration();

                var currentConfig = GetConfiguration();
                ConfigurationChanged?.Invoke(this, currentConfig);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling configuration file change");
            }
        }

        public void Dispose()
        {
            _fileWatcher?.Dispose();
        }
    }
}

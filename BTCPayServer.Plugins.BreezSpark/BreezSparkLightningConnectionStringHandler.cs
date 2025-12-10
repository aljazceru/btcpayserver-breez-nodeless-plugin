using BTCPayServer.Lightning;
using NBitcoin;

namespace BTCPayServer.Plugins.BreezSpark;

public class BreezSparkLightningConnectionStringHandler : ILightningConnectionStringHandler
{
    private readonly BreezSparkService _breezService;

    public BreezSparkLightningConnectionStringHandler(BreezSparkService breezService)
    {
        _breezService = breezService;
    }
    public ILightningClient? Create(string connectionString, Network network, out string? error)
    {
        var kv = LightningConnectionStringHelper.ExtractValues(connectionString, out var type);
        if (type != "breezspark")
        {
            error = null;
            return null;
        }

        
        if (!kv.TryGetValue("key", out var key))
        {
            error = $"The key 'key' is mandatory for breezspark connection strings";
            return null;
        }
        
        error = null;
        return _breezService.GetClientByPaymentKey(key);
    }
}

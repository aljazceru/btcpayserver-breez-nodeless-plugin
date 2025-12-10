#nullable enable
using System;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.BreezSpark;

public class BreezSparkSettings
{
    public string? Mnemonic { get; set; }
    public string? ApiKey { get; set; }

    public string PaymentKey { get; set; } = Guid.NewGuid().ToString();
}

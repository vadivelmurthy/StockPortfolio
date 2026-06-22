using System.Text.Json;

namespace IrelandTaxTracker.Services;

public class PriceService
{
    private readonly HttpClient _http = new();
    private decimal? _usdToEur;
    private DateTime _usdToEurFetched = DateTime.MinValue;
    private readonly Dictionary<string, (decimal price, DateTime fetched)> _stockCache = new();
    private const decimal GramsPerTroyOz = 31.1034768m;

    public decimal? LastUsdToEur => _usdToEur;

    // Frankfurter is a free, key-free FX API (ECB rates)
    public async Task<decimal?> GetUsdToEurAsync()
    {
        try
        {
            var json = await _http.GetStringAsync("https://api.frankfurter.app/latest?from=USD&to=EUR");
            using var doc = JsonDocument.Parse(json);
            var rate = doc.RootElement.GetProperty("rates").GetProperty("EUR").GetDecimal();
            _usdToEur = rate;
            _usdToEurFetched = DateTime.Now;
            return rate;
        }
        catch
        {
            return _usdToEur; // fall back to last known value if the API call fails
        }
    }

    // Yahoo Finance's public quote endpoint - no key required, returns regularMarketPrice
    public async Task<decimal?> GetStockPriceAsync(string symbol)
    {
        if (_stockCache.TryGetValue(symbol, out var cached) && (DateTime.Now - cached.fetched).TotalSeconds < 30)
            return cached.price;

        try
        {
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0");
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return cached.price;

            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var price = doc.RootElement
                .GetProperty("chart").GetProperty("result")[0]
                .GetProperty("meta").GetProperty("regularMarketPrice").GetDecimal();

            _stockCache[symbol] = (price, DateTime.Now);
            if (symbol.EndsWith(".L", StringComparison.OrdinalIgnoreCase) && price > 1000)
                price = price / 100;
            return price;
        }
        catch
        {
            return cached.price; // returns 0 if never fetched successfully
        }
    }

    public async Task<decimal?> GetMetalPricePerGramEurAsync(string metalTicker)
    {
        var usdPerOz = await GetStockPriceAsync(metalTicker); // GC=F or SI=F
        if (!usdPerOz.HasValue) return null;
        var eurRate = await GetUsdToEurAsync();
        if (!eurRate.HasValue) return null;
        var usdPerGram = usdPerOz.Value / GramsPerTroyOz;
        return usdPerGram * eurRate.Value;
    }
}

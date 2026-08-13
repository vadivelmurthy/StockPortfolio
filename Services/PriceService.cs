using System.Text.Json;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace IrelandTaxTracker.Services;

public class PriceService
{
    private readonly HttpClient _http = new();
    private decimal? _usdToEur;
    private DateTime _usdToEurFetched = DateTime.MinValue;
    private readonly Dictionary<string, (decimal price, DateTime fetched)> _stockCache = new();
    private const decimal GramsPerTroyOz = 31.1034768m;
    private decimal _gbpToEur = 1.17m;
    private DateTime _gbpToEurFetched = DateTime.MinValue;

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
        // Return from cache if fetched within last 30 seconds
        if (_stockCache.TryGetValue(symbol, out var cached) &&
            (DateTime.Now - cached.fetched).TotalSeconds < 30)
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
            var meta = doc.RootElement
               .GetProperty("chart")
               .GetProperty("result")[0]
               .GetProperty("meta");
            // Prefer the most recent price: post-market > pre-market > regular
            decimal price;
            if (meta.TryGetProperty("postMarketPrice", out var post) && post.ValueKind == JsonValueKind.Number && post.GetDecimal() > 0)
            {
                price = post.GetDecimal();
            }
            else if (meta.TryGetProperty("preMarketPrice", out var pre) && pre.ValueKind == JsonValueKind.Number && pre.GetDecimal() > 0)
            {
                price = pre.GetDecimal();
            }
            else
            {
                price = meta.GetProperty("regularMarketPrice").GetDecimal();
            }

            // ── Metal tickers: handled separately via GetMetalPricePerGramEurAsync
            // No conversion here, return raw USD/oz price
            if (IsMetalTicker(symbol))
            {
                _stockCache[symbol] = (price, DateTime.Now);
                return price;
            }

            // ── London GBX tickers (pence): divide by 100 → GBP, then × GBP/EUR
            if (IsGBXTicker(symbol))
            {
                decimal gbp = price / 100m;                      // pence → pounds
                decimal gbpEur = await GetGbpToEurAsync() ?? 1.17m;       // live FX
                decimal eur = gbp * gbpEur;
                _stockCache[symbol] = (eur, DateTime.Now);
                return eur;
            }

            // ── Other .L tickers (e.g. ETFs quoting in GBP, not pence)
            // Keep the old heuristic as safety net just in case
            if (symbol.EndsWith(".L", StringComparison.OrdinalIgnoreCase))
            {
                if (price > 500) price = price / 100m;           // likely pence
                decimal gbpEur = await GetGbpToEurAsync() ?? 1.17m;
                price = price * gbpEur;
                _stockCache[symbol] = (price, DateTime.Now);
                return price;
            }

            // ── USD stocks (UNH, NVDA, EVTL etc): return raw, portfolio converts via USD/EUR
            _stockCache[symbol] = (price, DateTime.Now);
            return price;
        }
        catch
        {
            return cached.price; // fall back to last known value
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

    public async Task<List<(DateOnly date, decimal amount)>> GetRecentDividendsAsync(string symbol, DateOnly from)
    {
        var result = new List<(DateOnly, decimal)>();
        try
        {
            var fromUnix = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue)).ToUnixTimeSeconds();
            var toUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?period1={fromUnix}&period2={toUnix}&interval=1d&events=div%2Csplits";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0");
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return result;
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement
                .GetProperty("chart")
                .GetProperty("result")[0];
            if (!root.TryGetProperty("events", out var events)) return result;
            if (!events.TryGetProperty("dividends", out var dividends)) return result;
            foreach (var div in dividends.EnumerateObject())
            {
                var timestamp = div.Value.GetProperty("date").GetInt64();
                var amount = div.Value.GetProperty("amount").GetDecimal();
                if (IsGBXTicker(symbol))
                {
                    decimal gbp = amount / 100m;
                    decimal gbpEur = await GetGbpToEurAsync() ?? 1.17m;
                    amount = gbp * gbpEur;
                }
                var date = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(timestamp).Date);
                result.Add((date, amount));
            }
        }
        catch { }
        return result;
    }

    public async Task<(DateOnly? exDate, DateOnly? payDate, decimal amount)?> GetUpcomingDividendAsync(string symbol)
    {
        try
        {
            // Try v7 quote endpoint first — returns exDividendDate reliably for most tickers
            var url = $"https://query1.finance.yahoo.com/v7/finance/quote?symbols={Uri.EscapeDataString(symbol)}&fields=exDividendDate,dividendDate,trailingAnnualDividendRate,forwardDividend";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0");
            var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var result = doc.RootElement
                    .GetProperty("quoteResponse")
                    .GetProperty("result");
                if (result.GetArrayLength() > 0)
                {
                    var quote = result[0];
                    DateOnly? exDate = null;
                    DateOnly? payDate = null;
                    decimal amount = 0;
                    if (quote.TryGetProperty("exDividendDate", out var exDiv) && exDiv.ValueKind != JsonValueKind.Null)
                        exDate = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(exDiv.GetInt64()).Date);
                    if (quote.TryGetProperty("dividendDate", out var divDate) && divDate.ValueKind != JsonValueKind.Null)
                        payDate = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(divDate.GetInt64()).Date);
                    if (quote.TryGetProperty("trailingAnnualDividendRate", out var rate) && rate.ValueKind != JsonValueKind.Null)
                        amount = rate.GetDecimal() / 4;
                    if (IsGBXTicker(symbol))
                    {
                        decimal gbp = amount / 100m;
                        decimal gbpEur = await GetGbpToEurAsync() ?? 1.17m;
                        amount = gbp * gbpEur;
                    }

                    if (amount > 0 || exDate.HasValue)
                        return (exDate, payDate, amount);
                }
            }
            // Fallback: chart endpoint
            var fromUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var toUnix = DateTimeOffset.UtcNow.AddDays(90).ToUnixTimeSeconds();
            var chartUrl = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?period1={fromUnix}&period2={toUnix}&interval=1d&events=div";
            using var req2 = new HttpRequestMessage(HttpMethod.Get, chartUrl);
            req2.Headers.UserAgent.ParseAdd("Mozilla/5.0");
            var resp2 = await _http.SendAsync(req2);
            if (!resp2.IsSuccessStatusCode) return null;
            var json2 = await resp2.Content.ReadAsStringAsync();
            using var doc2 = JsonDocument.Parse(json2);
            var root = doc2.RootElement.GetProperty("chart").GetProperty("result")[0];
            if (root.TryGetProperty("events", out var events) &&
                events.TryGetProperty("dividends", out var divs))
            {
                var first = divs.EnumerateObject().FirstOrDefault();
                if (first.Value.ValueKind != JsonValueKind.Undefined)
                {
                    var ts = first.Value.GetProperty("date").GetInt64();
                    var amt = first.Value.GetProperty("amount").GetDecimal();
                    if (IsGBXTicker(symbol))
                    {
                        decimal gbp = amt / 100m;
                        decimal gbpEur = await GetGbpToEurAsync() ?? 1.17m;
                        amt = gbp * gbpEur;
                    }

                    var exDate = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(ts).Date);
                    return (exDate, null, amt);
                }
            }
            // Last fallback: meta fields
            var meta = root.GetProperty("meta");
            DateOnly? metaExDate = null;
            DateOnly? metaPayDate = null;
            decimal metaAmount = 0;
            if (meta.TryGetProperty("exDividendDate", out var exD) && exD.ValueKind != JsonValueKind.Null)
                metaExDate = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(exD.GetInt64()).Date);
            if (meta.TryGetProperty("dividendDate", out var pD) && pD.ValueKind != JsonValueKind.Null)
                metaPayDate = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(pD.GetInt64()).Date);
            if (meta.TryGetProperty("trailingAnnualDividendRate", out var r))
                metaAmount = r.GetDecimal() / 4;
            if (IsGBXTicker(symbol))
            {
                decimal gbp = metaAmount / 100m;
                decimal gbpEur = await GetGbpToEurAsync() ?? 1.17m;
                metaAmount = gbp * gbpEur;
            }
            if (metaExDate.HasValue || metaAmount > 0)
                return (metaExDate, metaPayDate, metaAmount);
        }
        catch { }
        return null;
    }

    public async Task<decimal?> GetGbpToEurAsync()
    {
        if ((DateTime.Now - _gbpToEurFetched).TotalMinutes < 10)
            return _gbpToEur;

        try
        {
            var url = "https://api.frankfurter.app/latest?from=GBP&to=EUR";
            var doc = await _http.GetFromJsonAsync<JsonElement>(url);
            var rate = doc.GetProperty("rates").GetProperty("EUR").GetDecimal();
            _gbpToEur = rate;
            _gbpToEurFetched = DateTime.Now;
            return rate;
        }
        catch
        {
            return _gbpToEur; // fall back to last known
        }
    }

    private bool IsGBXTicker(string symbol)
    {
        var gbxTickers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
           {
               "NWG.L",
               "AZN.L",

           };
        return gbxTickers.Contains(symbol);
    }
    private bool IsMetalTicker(string? ticker) => ticker == "GC=F" || ticker == "SI=F";

}

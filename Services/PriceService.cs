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
    private string? _yahooCookie;
    private string? _yahooCrumb;
    private DateTime _crumbFetched = DateTime.MinValue;
    private const string AlphaVantageApiKey = "BZ6GYA1F2V8Q2LAF";
    private readonly Dictionary<string, ((DateOnly? exDate, DateOnly? payDate, decimal amount)? result, DateTime fetched)> _dividendCache = new();

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
            //Try v7 quote endpoint first — returns exDividendDate reliably for most tickers

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

    //public async Task<(DateOnly? exDate, DateOnly? payDate, decimal amount)?> GetUpcomingDividendAsync(string symbol)
    //{
    //    try
    //    {
    //        var url = $"https://api.twelvedata.com/dividends?symbol={Uri.EscapeDataString(symbol)}&apikey={TwelveDataApiKey}";

    //        using var req = new HttpRequestMessage(HttpMethod.Get, url);
    //        var resp = await _http.SendAsync(req);

    //        Console.WriteLine($"[DEBUG] Twelve Data dividend request for {symbol}: {resp.StatusCode}");

    //        var json = await resp.Content.ReadAsStringAsync();

    //        if (!resp.IsSuccessStatusCode)
    //        {
    //            Console.WriteLine($"[ERROR] Twelve Data dividend fetch failed for {symbol}: {resp.StatusCode} - {json}");
    //            return null;
    //        }

    //        using var doc = JsonDocument.Parse(json);
    //        var root = doc.RootElement;

    //        // Twelve Data returns an error object (status/code/message) instead of data on failure,
    //        // even with a 200 status code — check for that explicitly
    //        if (root.TryGetProperty("status", out var statusProp) && statusProp.GetString() == "error")
    //        {
    //            var message = root.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : "unknown error";
    //            Console.WriteLine($"[ERROR] Twelve Data API error for {symbol}: {message}");
    //            return null;
    //        }

    //        if (!root.TryGetProperty("dividends", out var dividendsArray) || dividendsArray.ValueKind != JsonValueKind.Array || dividendsArray.GetArrayLength() == 0)
    //        {
    //            Console.WriteLine($"[DEBUG] Twelve Data returned no dividend records for {symbol}");
    //            return null;
    //        }

    //        DateOnly? bestExDate = null;
    //        DateOnly? bestPayDate = null;
    //        decimal bestAmount = 0;
    //        bool foundFuture = false;
    //        var today = DateOnly.FromDateTime(DateTime.UtcNow);

    //        foreach (var entry in dividendsArray.EnumerateArray())
    //        {
    //            if (!entry.TryGetProperty("ex_date", out var exProp) || exProp.ValueKind != JsonValueKind.String)
    //                continue;
    //            if (!DateOnly.TryParse(exProp.GetString(), out var exDate))
    //                continue;

    //            decimal amount = 0;
    //            if (entry.TryGetProperty("amount", out var amtProp))
    //            {
    //                if (amtProp.ValueKind == JsonValueKind.Number)
    //                    amount = amtProp.GetDecimal();
    //                else if (amtProp.ValueKind == JsonValueKind.String && decimal.TryParse(amtProp.GetString(), out var parsedAmt))
    //                    amount = parsedAmt;
    //            }

    //            DateOnly? payDate = null;
    //            if (entry.TryGetProperty("payment_date", out var payProp) && payProp.ValueKind == JsonValueKind.String
    //                && DateOnly.TryParse(payProp.GetString(), out var pd))
    //                payDate = pd;

    //            if (exDate >= today)
    //            {
    //                // Genuinely upcoming, already-declared dividend — prefer the earliest future one
    //                if (!foundFuture || exDate < bestExDate)
    //                {
    //                    bestExDate = exDate;
    //                    bestPayDate = payDate;
    //                    bestAmount = amount;
    //                    foundFuture = true;
    //                }
    //            }
    //            else if (!foundFuture)
    //            {
    //                // No future dividend found yet — track the most recent past one as a fallback estimate
    //                if (bestExDate == null || exDate > bestExDate)
    //                {
    //                    bestExDate = exDate;
    //                    bestPayDate = payDate;
    //                    bestAmount = amount;
    //                }
    //            }
    //        }

    //        if (bestAmount <= 0 && bestExDate == null)
    //            return null;

    //        // Twelve Data returns amounts in the security's native trading currency (GBP for .L tickers,
    //        // USD for US tickers, EUR for .AS tickers) — convert only currency, never divide by 100.
    //        if (IsGBXTicker(symbol) && bestAmount > 0)
    //        {
    //            decimal gbpEur = await GetGbpToEurAsync() ?? 1.17m;
    //            bestAmount = bestAmount * gbpEur;
    //        }

    //        return (bestExDate, bestPayDate, bestAmount);
    //    }
    //    catch (Exception ex)
    //    {
    //        Console.WriteLine($"[ERROR] GetUpcomingDividendAsync (Twelve Data) failed for {symbol}: {ex.GetType().Name} - {ex.Message}");
    //        return null;
    //    }
    //}

    //public async Task<(DateOnly? exDate, DateOnly? payDate, decimal amount)?> GetUpcomingDividendAsync(string symbol)
    //  {
    //    // Serve from cache if fetched within the last 24 hours — critical given the 25 req/day free limit
    //    if (_dividendCache.TryGetValue(symbol, out var cached) &&
    //        (DateTime.Now - cached.fetched).TotalHours < 24)
    //    {
    //        return cached.result;
    //    }

    //    try
    //    {
    //        var url = $"https://www.alphavantage.co/query?function=DIVIDENDS&symbol={Uri.EscapeDataString(symbol)}&apikey={AlphaVantageApiKey}";

    //        using var req = new HttpRequestMessage(HttpMethod.Get, url);
    //        var resp = await _http.SendAsync(req);

    //        Console.WriteLine($"[DEBUG] Alpha Vantage dividend request for {symbol}: {resp.StatusCode}");

    //        var json = await resp.Content.ReadAsStringAsync();

    //        if (!resp.IsSuccessStatusCode)
    //        {
    //            Console.WriteLine($"[ERROR] Alpha Vantage dividend fetch failed for {symbol}: {resp.StatusCode} - {json}");
    //            return null;
    //        }

    //        using var doc = JsonDocument.Parse(json);
    //        var root = doc.RootElement;

    //        // Alpha Vantage returns a 200 with a "Note" or "Information" field instead of a proper
    //        // error code when you hit the rate limit — check for that explicitly
    //        if (root.TryGetProperty("Note", out var noteProp))
    //        {
    //            Console.WriteLine($"[ERROR] Alpha Vantage rate limit hit for {symbol}: {noteProp.GetString()}");
    //            return null;
    //        }
    //        if (root.TryGetProperty("Information", out var infoProp))
    //        {
    //            Console.WriteLine($"[ERROR] Alpha Vantage info/limit message for {symbol}: {infoProp.GetString()}");
    //            return null;
    //        }

    //        if (!root.TryGetProperty("data", out var dataArray) || dataArray.ValueKind != JsonValueKind.Array || dataArray.GetArrayLength() == 0)
    //        {
    //            Console.WriteLine($"[DEBUG] Alpha Vantage returned no dividend records for {symbol}");
    //            _dividendCache[symbol] = (null, DateTime.Now);
    //            return null;
    //        }

    //        DateOnly? bestExDate = null;
    //        DateOnly? bestPayDate = null;
    //        decimal bestAmount = 0;
    //        bool foundFuture = false;
    //        var today = DateOnly.FromDateTime(DateTime.UtcNow);

    //        foreach (var entry in dataArray.EnumerateArray())
    //        {
    //            if (!entry.TryGetProperty("ex_dividend_date", out var exProp) || exProp.ValueKind != JsonValueKind.String)
    //                continue;
    //            if (!DateOnly.TryParse(exProp.GetString(), out var exDate))
    //                continue;

    //            decimal amount = 0;
    //            if (entry.TryGetProperty("amount", out var amtProp) && amtProp.ValueKind == JsonValueKind.String
    //                && decimal.TryParse(amtProp.GetString(), out var parsedAmt))
    //                amount = parsedAmt;

    //            DateOnly? payDate = null;
    //            if (entry.TryGetProperty("payment_date", out var payProp) && payProp.ValueKind == JsonValueKind.String
    //                && DateOnly.TryParse(payProp.GetString(), out var pd))
    //                payDate = pd;

    //            if (exDate >= today)
    //            {
    //                if (!foundFuture || exDate < bestExDate)
    //                {
    //                    bestExDate = exDate;
    //                    bestPayDate = payDate;
    //                    bestAmount = amount;
    //                    foundFuture = true;
    //                }
    //            }
    //            else if (!foundFuture)
    //            {
    //                if (bestExDate == null || exDate > bestExDate)
    //                {
    //                    bestExDate = exDate;
    //                    bestPayDate = payDate;
    //                    bestAmount = amount;
    //                }
    //            }
    //        }

    //        (DateOnly?, DateOnly?, decimal)? result = null;
    //        if (bestAmount > 0 || bestExDate.HasValue)
    //        {
    //            // Alpha Vantage returns amounts in the security's native trading currency —
    //            // convert only currency for GBX-listed tickers, never divide by 100.
    //            if (IsGBXTicker(symbol) && bestAmount > 0)
    //            {
    //                decimal gbpEur = await GetGbpToEurAsync() ?? 1.17m;
    //                bestAmount = bestAmount * gbpEur;
    //            }
    //            result = (bestExDate, bestPayDate, bestAmount);
    //        }

    //        _dividendCache[symbol] = (result, DateTime.Now);
    //        return result;
    //    }
    //    catch (Exception ex)
    //    {
    //        Console.WriteLine($"[ERROR] GetUpcomingDividendAsync (Alpha Vantage) failed for {symbol}: {ex.GetType().Name} - {ex.Message}");
    //        return null;
    //    }
    //}

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

    private async Task EnsureYahooAuthAsync()
    {
        if (_yahooCrumb != null && (DateTime.Now - _crumbFetched).TotalHours < 1)
            return;

        var cookieReq = new HttpRequestMessage(HttpMethod.Get, "https://fc.yahoo.com");
        cookieReq.Headers.UserAgent.ParseAdd("Mozilla/5.0");
        var cookieResp = await _http.SendAsync(cookieReq);
        if (cookieResp.Headers.TryGetValues("Set-Cookie", out var cookies))
            _yahooCookie = string.Join("; ", cookies.Select(c => c.Split(';')[0]));

        var crumbReq = new HttpRequestMessage(HttpMethod.Get, "https://query1.finance.yahoo.com/v1/test/getcrumb");
        crumbReq.Headers.UserAgent.ParseAdd("Mozilla/5.0");
        if (_yahooCookie != null)
            crumbReq.Headers.Add("Cookie", _yahooCookie);
        var crumbResp = await _http.SendAsync(crumbReq);
        if (crumbResp.IsSuccessStatusCode)
        {
            _yahooCrumb = await crumbResp.Content.ReadAsStringAsync();
            _crumbFetched = DateTime.Now;
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

    private static readonly HashSet<string> SemiAnnualTickers = new(StringComparer.OrdinalIgnoreCase)
    {
        "AZN", "NWG.L"
    };


}

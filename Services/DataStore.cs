using System.Text.Json;
using IrelandTaxTracker.Models;

namespace IrelandTaxTracker.Services;

public class DataStore
{
    private readonly string _filePath;
    private readonly object _lock = new();
    public AppData Data { get; private set; }

    public event Action? OnChange;

    public DataStore()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.GetFiles("*.csproj").Any())
            dir = dir.Parent;
        var projectRoot = dir?.FullName ?? AppContext.BaseDirectory;
        _filePath = Path.Combine(projectRoot, "data.json");
        Data = Load();
        SeedTargetsIfEmpty();
    }

    private AppData Load()
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<AppData>(json);
                if (loaded != null) return loaded;
            }
            catch
            {
                // fall through to seeded defaults if file is corrupt
            }
        }
        return SeedDefaults();
    }

    private AppData SeedDefaults()
    {
        var data = new AppData();
        // Holdings — one per stock/ETF/metal
        var nvda = new PortfolioHolding { Name = "NVIDIA", TickerSymbol = "NVDA", StartDate = new DateOnly(2026, 2, 6), Currency = "USD" };
        var evtl = new PortfolioHolding { Name = "Vertical Aerospace", TickerSymbol = "EVTL", StartDate = new DateOnly(2026, 2, 6), Currency = "USD" };
        var unh = new PortfolioHolding { Name = "UnitedHealth", TickerSymbol = "UNH", StartDate = new DateOnly(2026, 2, 6), Currency = "USD" };
        var vusa = new PortfolioHolding { Name = "Vanguard S&P 500", TickerSymbol = "VUSA.AS", StartDate = new DateOnly(2026, 2, 6), Currency = "EUR" };
        var iusa = new PortfolioHolding { Name = "iShares Core S&P 500", TickerSymbol = "IUSA.AS", StartDate = new DateOnly(2026, 2, 6), Currency = "EUR" };
        var gold = new PortfolioHolding { Name = "Gold (XAU)", TickerSymbol = "GC=F", StartDate = new DateOnly(2026, 3, 6), Currency = "EUR" };
        var silver = new PortfolioHolding { Name = "Silver (XAG)", TickerSymbol = "SI=F", StartDate = new DateOnly(2026, 3, 6), Currency = "EUR" };
        data.Holdings.AddRange(new[] { nvda, evtl, unh, vusa, iusa, gold, silver });
        // NVDA transactions
        void AddBuy(PortfolioHolding h, string date, decimal amount, decimal qty) =>
            data.Transactions.Add(new Transaction { HoldingId = h.Id, Date = DateOnly.Parse(date), Amount = amount, QuantityReceived = qty });
        AddBuy(nvda, "2026-06-01", 60m, 0.27111774m);
        AddBuy(nvda, "2026-05-01", 60m, 0.30273230m);
        AddBuy(nvda, "2026-04-15", 1.61m, 0.00819172m);
        AddBuy(nvda, "2026-04-06", 30m, 0.16973093m);
        AddBuy(nvda, "2026-03-06", 30m, 0.16510732m);
        AddBuy(nvda, "2026-02-06", 29.99m, 0.15658885m);
        AddBuy(nvda, "2026-02-06", 29.99m, 0.15659311m);
        AddBuy(nvda, "2026-02-06", 24.99m, 0.13056233m);
        AddBuy(nvda, "2026-02-06", 29.99m, 0.15764706m);
        AddBuy(nvda, "2026-02-06", 29.99m, 0.15750055m);
        AddBuy(nvda, "2026-02-06", 2.49m, 0.00721286m);
        AddBuy(nvda, "2026-02-06", 2m, 0.01132567m);
        // NVDA sells (Feb 6 test trades) — negative quantity/amount
        data.Transactions.Add(new Transaction { HoldingId = nvda.Id, Date = DateOnly.Parse("2026-02-06"), Amount = -2m, QuantityReceived = -0.0174852m });
        data.Transactions.Add(new Transaction { HoldingId = nvda.Id, Date = DateOnly.Parse("2026-02-06"), Amount = -79.98m, QuantityReceived = -0.44580496m });
        // EVTL transactions
        AddBuy(evtl, "2026-06-08", 107m, 47.67727033m);
        AddBuy(evtl, "2026-06-01", 60m, 22.64150943m);
        AddBuy(evtl, "2026-05-01", 60m, 26.20087336m);
        AddBuy(evtl, "2026-04-15", 18.99m, 6.64552239m);
        AddBuy(evtl, "2026-04-06", 30m, 13.04347826m);
        AddBuy(evtl, "2026-03-06", 30m, 7.17703349m);
        AddBuy(evtl, "2026-02-06", 29.99m, 6.53287982m);
        AddBuy(evtl, "2026-02-06", 29.99m, 6.54772727m);
        AddBuy(evtl, "2026-02-06", 2.49m, 0.30045872m);
        AddBuy(evtl, "2026-02-06", 2m, 0.46728972m);
        // UNH transactions
        AddBuy(unh, "2026-06-01", 60m, 0.15667433m);
        AddBuy(unh, "2026-05-01", 60m, 0.16275600m);
        AddBuy(unh, "2026-04-15", 1m, 0.00316456m);
        AddBuy(unh, "2026-04-06", 30m, 0.10642449m);
        AddBuy(unh, "2026-03-06", 30m, 0.10513773m);
        AddBuy(unh, "2026-02-06", 29.99m, 0.10593859m);
        AddBuy(unh, "2026-02-06", 29.99m, 0.10603997m);
        AddBuy(unh, "2026-02-06", 2.49m, 0.00480558m);
        AddBuy(unh, "2026-02-06", 2m, 0.00746269m);
        // VUSA transactions
        AddBuy(vusa, "2026-06-01", 60m, 0.48475055m);
        AddBuy(vusa, "2026-05-04", 60m, 0.51287311m);
        AddBuy(vusa, "2026-04-15", 30m, 0.26774239m);
        AddBuy(vusa, "2026-04-07", 30m, 0.27729507m);
        AddBuy(vusa, "2026-03-06", 30m, 0.26841314m);
        AddBuy(vusa, "2026-02-06", 30m, 0.27135078m);
        AddBuy(vusa, "2026-02-06", 30m, 0.26228203m);
        AddBuy(vusa, "2026-02-06", 1m, 0.00905797m);
        AddBuy(vusa, "2026-02-06", 1m, 0.00905846m);
        AddBuy(vusa, "2026-02-06", 2m, 0.01821759m);
        AddBuy(vusa, "2026-02-06", 2m, 0.01821427m);
        // IUSA transactions
        AddBuy(iusa, "2026-06-01", 60m, 0.92222563m);
        AddBuy(iusa, "2026-05-04", 60m, 0.97583348m);
        AddBuy(iusa, "2026-04-17", 60m, 1.00695977m);
        AddBuy(iusa, "2026-04-15", 1.88m, 0.03192181m);
        AddBuy(iusa, "2026-04-07", 30m, 0.52659386m);
        AddBuy(iusa, "2026-04-02", 30m, 0.53041393m);
        AddBuy(iusa, "2026-02-06", 3m, 0.03435788m);
        // Gold transactions
        AddBuy(gold, "2026-06-19", 300m, 0.080363m);
        AddBuy(gold, "2026-06-06", 300m, 0.077493m);
        AddBuy(gold, "2026-05-20", 150m, 0.037944m);
        AddBuy(gold, "2026-05-13", 150m, 0.036642m);
        AddBuy(gold, "2026-04-27", 150m, 0.036808m);
        AddBuy(gold, "2026-04-18", 65.52m, 0.015304m);
        AddBuy(gold, "2026-04-17", 150m, 0.036180m);
        AddBuy(gold, "2026-04-15", 10m, 0.002173m);
        AddBuy(gold, "2026-04-01", 150m, 0.036100m);
        AddBuy(gold, "2026-03-23", 200m, 0.052098m);
        AddBuy(gold, "2026-03-21", 100m, 0.024787m);
        AddBuy(gold, "2026-03-18", 50m, 0.011442m);
        AddBuy(gold, "2026-03-08", 20m, 0.004151m);
        AddBuy(gold, "2026-03-06", 30m, 0.006462m);
        // Silver transactions
        AddBuy(silver, "2026-06-19", 100m, 1.694689m);
        AddBuy(silver, "2026-06-06", 100m, 1.629226m);
        AddBuy(silver, "2026-05-20", 50m, 0.740391m);
        AddBuy(silver, "2026-05-13", 50m, 0.636432m);
        AddBuy(silver, "2026-04-27", 50m, 0.755568m);
        AddBuy(silver, "2026-04-17", 50m, 0.722580m);
        AddBuy(silver, "2026-03-23", 100m, 1.707288m);
        AddBuy(silver, "2026-03-18", 50m, 0.726609m);
        AddBuy(silver, "2026-03-08", 20m, 0.252123m);
        AddBuy(silver, "2026-03-06", 30m, 0.392711m);
        // ESPP cycles
        var vestDates = new[]
        {
           new DateOnly(2026,6,30), new DateOnly(2026,12,31),
           new DateOnly(2027,6,30), new DateOnly(2027,12,31),
           new DateOnly(2028,6,30), new DateOnly(2028,12,31),
           new DateOnly(2029,6,30), new DateOnly(2029,12,31),
           new DateOnly(2030,6,30), new DateOnly(2030,12,31),
       };
        foreach (var d in vestDates)
            data.EsppCycles.Add(new EsppCycle { VestDate = d });
        // CGT sales (NVDA test sells Feb 6)
        data.CgtSales.Add(new CgtSale { DateSold = new DateOnly(2026, 2, 6), StockOrEtf = "NVDA", Quantity = 0.44580496m, CostBasis = Math.Round(176.59m * 0.44580496m, 2), SalePrice = Math.Round(182.08m * 0.44580496m, 2) });
        data.CgtSales.Add(new CgtSale { DateSold = new DateOnly(2026, 2, 6), StockOrEtf = "NVDA", Quantity = 0.0174852m, CostBasis = Math.Round(176.59m * 0.0174852m, 2), SalePrice = Math.Round(182.44m * 0.0174852m, 2) });
        Save(data);
        return data;
    }
    public void Save(AppData? data = null)
    {
        lock (_lock)
        {
            data ??= Data;
            Data = data;
            var json = JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        OnChange?.Invoke();
    }



    private void SeedTargetsIfEmpty()
    {
        var targets = new Dictionary<string, (decimal target, decimal monthly)>
        {
            ["NVDA"] = (30, 60),
            ["EVTL"] = (300, 0),    // stopped
            ["UNH"] = (15, 60),
            ["VUSA.AS"] = (40, 60),
            ["IUSA.AS"] = (75, 60),
            ["NWG.L"] = (650, 90),
            ["AZN"] = (35, 90),
            ["SPCX"] = (50, 90),
            ["GC=F"] = (1000, 320),  // 1kg gold in grams
            ["SI=F"] = (3000, 100),  // 3kg silver in grams
        };
        foreach (var h in Data.Holdings)
        {
            if (targets.TryGetValue(h.TickerSymbol, out var t))
            {
                if (h.TargetQuantity == 0) h.TargetQuantity = t.target;
                if (h.MonthlyContribution == 0) h.MonthlyContribution = t.monthly;
            }
        }
        Save();
    }

    public string FilePath => _filePath;
}

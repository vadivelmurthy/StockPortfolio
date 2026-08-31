using IrelandTaxTracker.Models;
using System.Linq;

namespace IrelandTaxTracker.Pages.Index
{
    public partial class Index
    {
        private decimal MonthlyContribution = 1000;
        private decimal AnnualReturnPct => AssumedReturnOverride ?? ActualReturnPct();
        private Timer? _timer;
        private DateTime? _lastRefresh;
        private bool _fxError;
        private Dictionary<string, decimal> _livePrices = new();
        private Dictionary<string, decimal> _metalPricesPerGramEur = new();
        private Dictionary<string, (DateOnly? exDate, DateOnly? payDate, decimal amount)> _upcomingDividends = new();
        private decimal? ManualOverride = null;
        private DateOnly ExitDate = new DateOnly(2034, 2, 1);
        private decimal? AssumedReturnOverride;

        protected override async Task OnInitializedAsync()
        {
            await RefreshNow();
            await FetchUpcomingDividendsAsync();
            _timer = new Timer(async _ => await InvokeAsync(RefreshNow), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));

        }

        private async Task RefreshNow()
        {
            var rate = await Prices.GetUsdToEurAsync();
            if (rate.HasValue)
            {
                Store.Data.UsdToEurRate = rate.Value;
                _fxError = false;
            }
            else
            {
                _fxError = true;
            }
            var gbpRate = await Prices.GetGbpToEurAsync();
            if (gbpRate.HasValue)
                Store.Data.GbpToEurRate = gbpRate.Value;

            var allTickers = Store.Data.Holdings.Select(h => h.TickerSymbol).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct();
            foreach (var t in allTickers)
            {
                if (IsMetalTicker(t))
                {
                    var pricePerGram = await Prices.GetMetalPricePerGramEurAsync(t);
                    if (pricePerGram.HasValue) _metalPricesPerGramEur[t] = pricePerGram.Value;
                }
                else
                {
                    var price = await Prices.GetStockPriceAsync(t);
                    if (price.HasValue) _livePrices[t] = price.Value;
                }
            }

            await FetchAndSaveDividendsAsync();
            AssumedReturnOverride ??= Math.Round(ActualReturnPct() ,2);
            // await FetchUpcomingDividendsAsync();
            _lastRefresh = DateTime.Now;
            Store.Save();
            StateHasChanged();
        }

        private decimal ActualUnits(PortfolioHolding h) =>
            Store.Data.Transactions.Where(t => t.HoldingId == h.Id).Sum(t => t.QuantityReceived);

        private decimal InvestedNative(PortfolioHolding h) =>
               Store.Data.Transactions.Where(t => t.HoldingId == h.Id).Sum(t => t.NetAmount);

        private decimal TotalFees(PortfolioHolding h) =>
            Store.Data.Transactions.Where(t => t.HoldingId == h.Id).Sum(t => t.Fee);

        private decimal InvestedEur(PortfolioHolding h) =>
            h.Currency == "USD" ? InvestedNative(h) * Store.Data.UsdToEurRate : InvestedNative(h);

        private decimal ToEur(PortfolioHolding h)
        {
            if (IsMetalTicker(h.TickerSymbol) && _metalPricesPerGramEur.TryGetValue(h.TickerSymbol, out var gpg) && gpg > 0)
                return ActualUnits(h) * 31.1034768m * gpg;
            if (_livePrices.TryGetValue(h.TickerSymbol, out var price) && price > 0)
            {
                var native = ActualUnits(h) * price;
                return h.Currency == "USD" ? native * Store.Data.UsdToEurRate : native;
            }
            return InvestedEur(h); // fallback to invested if no live price yet
        }

        private decimal Pnl(PortfolioHolding h) => ToEur(h) - InvestedEur(h);

        private string PnlClass(PortfolioHolding h) => Pnl(h) < 0 ? "neg" : "pos";

        private string PnlText(PortfolioHolding h)
        {
            var invested = InvestedEur(h);
            if (invested == 0) return "—";
            var pnl = Pnl(h);
            var pct = invested != 0 ? (pnl / invested) * 100 : 0;
            var sign = pnl >= 0 ? "+" : "";
            return $"{sign}€{pnl:N2} ({sign}{pct:N1}%)";
        }

        private decimal TotalInvestedEur() => Store.Data.Holdings.Sum(InvestedEur);

        private string TotalPnlClass() => (Total() - TotalInvestedEur()) < 0 ? "neg" : "pos";

        private string TotalPnlText()
        {
            var invested = TotalInvestedEur();
            if (invested == 0) return "—";
            var pnl = Total() - invested;
            var pct = (pnl / invested) * 100;
            var sign = pnl >= 0 ? "+" : "";
            return $"{sign}€{pnl:N2} ({sign}{pct:N1}%)";
        }

        private bool IsMetalTicker(string ticker) => ticker == "GC=F" || ticker == "SI=F";

        private decimal Total() => Store.Data.Holdings.Sum(ToEur);

        private decimal ParseDec(object? v) => decimal.TryParse(v?.ToString(), out var d) ? d : 0;

        private void UpdateName(PortfolioHolding h, object? v) { h.Name = v?.ToString() ?? ""; Save(); }

        private void UpdateCurrency(PortfolioHolding h, object? v) { h.Currency = v?.ToString() ?? "EUR"; Save(); }

        private void UpdateTicker(PortfolioHolding h, object? v) { h.TickerSymbol = v?.ToString()?.Trim().ToUpperInvariant() ?? ""; Save(); }

        private void AddHolding()
        {
            Store.Data.Holdings.Add(new PortfolioHolding { Name = "New holding", StartDate = DateOnly.FromDateTime(DateTime.Today) });
            Save();
        }

        private void GoToContribute() => Nav.NavigateTo("/contribute");

        private void GoToSell() => Nav.NavigateTo("/sell");

        private void Remove(PortfolioHolding h)
        {
            Store.Data.Holdings.Remove(h);
            Save();
        }

        private void Save() => Store.Save();

        private (int years, int months) ProjectTarget()
        {
            decimal value = GoalValue();
            decimal target = 250000m;
            decimal monthly = EffectiveMonthly();
            decimal monthlyRate = (AnnualReturnPct / 100m) / 12m;

            int months = 0;
            while (value < target && months < 1200)
            {
                value += monthly;
                value *= (1 + monthlyRate);
                months++;
            }
            return (months / 12, months % 12);
        }

        private int MonthsInvesting()
        {
            var tx = Store.Data.Transactions;
            if (!tx.Any()) return 1;
            var first = tx.Min(t => t.Date);
            var months = ((DateTime.Today.Year - first.Year) * 12)
                         + DateTime.Today.Month - first.Month;
            return Math.Max(1, months);
        }

        // Auto monthly = total invested (ex-ESPP) ÷ months investing
        private decimal AutoMonthly() =>
            Math.Round(GoalInvested() / MonthsInvesting(), 2);

        private decimal EffectiveMonthly() =>
            ManualOverride.HasValue && ManualOverride.Value > 0
                ? ManualOverride.Value
                : AutoMonthly();

        private bool CountsToGoal(PortfolioHolding h) => true;

        // Current value of goal-counting holdings (includes metals, excludes ESPP)
        private decimal GoalValue() =>
            Store.Data.Holdings.Where(CountsToGoal).Sum(h => ToEur(h));

        // Total invested into goal-counting holdings
        private decimal GoalInvested() =>
            Store.Data.Holdings.Where(CountsToGoal).Sum(h => InvestedEur(h));

        // Your ACTUAL return so far (a real fact, not an assumption)
        private decimal ActualReturnPct()
        {
            var invested = GoalInvested();
            if (invested == 0) return 0;
            return ((GoalValue() - invested) / invested) * 100m;
        }

        private int YearsToTarget() => ProjectTarget().years;

        private int MonthsToTarget() => ProjectTarget().months;

        public void Dispose() => _timer?.Dispose();

        private async Task FetchAndSaveDividendsAsync()
        {
            var stockHoldings = Store.Data.Holdings
                .Where(h => !string.IsNullOrWhiteSpace(h.TickerSymbol)
         && !IsMetalTicker(h.TickerSymbol))
                .ToList();
            foreach (var h in stockHoldings)
            {
                var firstDate = Store.Data.Transactions
                    .Where(t => t.HoldingId == h.Id)
                    .Select(t => t.Date)
                    .OrderBy(d => d)
                    .FirstOrDefault();
                if (firstDate == default) continue;
                var recent = await Prices.GetRecentDividendsAsync(h.TickerSymbol, firstDate);
                foreach (var (date, amountPerShare) in recent)
                {
                    // Skip future dividends
                    if (date > DateOnly.FromDateTime(DateTime.Today)) continue;
                    // Skip if already logged for this holding and date
                    var alreadyExists = Store.Data.Dividends.Any(d =>
                        d.HoldingId == h.Id &&
                        d.Date == date);
                    if (alreadyExists) continue;
                    // Calculate how many shares you held at that dividend date
                    var sharesAtDate = Store.Data.Transactions
                        .Where(t => t.HoldingId == h.Id && t.Date <= date)
                        .Sum(t => t.QuantityReceived);
                    if (sharesAtDate <= 0) continue;
                    // Total dividend = shares held × amount per share
                    var totalAmount = Math.Round(sharesAtDate * amountPerShare, 4);
                    // Withholding tax rates
                    var withholdingRate = h.Currency == "USD" ? 0.15m :
                                          h.Currency == "GBP" ? 0.20m : 0m;
                    var withholdingTax = Math.Round(totalAmount * withholdingRate, 4);
                    Store.Data.Dividends.Add(new Dividend
                    {
                        HoldingId = h.Id,
                        Date = date,
                        Amount = totalAmount,
                        Currency = h.Currency,
                        WithholdingTax = withholdingTax
                    });
                }
            }
        }

        private async Task FetchUpcomingDividendsAsync()
        {
            var stockTickers = Store.Data.Holdings
                .Where(h => !string.IsNullOrWhiteSpace(h.TickerSymbol) && !IsMetalTicker(h.TickerSymbol))
                .Select(h => h.TickerSymbol)
                .Distinct();
            foreach (var ticker in stockTickers)
            {
                var result = await Prices.GetUpcomingDividendAsync(ticker);
                if (result.HasValue)
                    _upcomingDividends[ticker] = result.Value;
            }
        }

        private bool IsSold(PortfolioHolding h) => !IsMetalTicker(h.TickerSymbol);

        private bool IsEtf(PortfolioHolding h) =>
        h.TickerSymbol == "VUSA.AS" || h.TickerSymbol == "IUSA.AS";

        private int SellCountToday() =>
        Store.Data.Holdings.Count(IsSold);

        private decimal SellValueNow() =>
            Store.Data.Holdings.Where(IsSold).Sum(h => ToEur(h));

        private decimal SellInvestedNow() =>
            Store.Data.Holdings.Where(IsSold).Sum(h => InvestedEur(h));

        private decimal TaxNow()
        {
            decimal tax = 0;
            foreach (var h in Store.Data.Holdings.Where(IsSold))
            {
                var gain = ToEur(h) - InvestedEur(h);
                if (gain > 0)
                    tax += gain * (IsEtf(h) ? 0.41m : 0.33m);
            }
            return tax;
        }

        private int MonthsToExit()
        {
            var months = ((ExitDate.Year - DateTime.Today.Year) * 12)
                         + ExitDate.Month - DateTime.Today.Month;
            return Math.Max(0, months);
        }

        private decimal ExitMonthly() => EffectiveMonthly();

        private decimal ProjectedValue2034()
        {
            decimal value = SellValueNow();
            decimal monthly = ExitMonthly();
            decimal rate = (AnnualReturnPct / 100m) / 12m;
            for (int m = 0; m < MonthsToExit(); m++)
            {
                value += monthly;
                value *= (1 + rate);
            }
            return value;
        }

        private decimal ProjectedInvested2034() =>
        SellInvestedNow() + ExitMonthly() * MonthsToExit();

        // Blended tax rate on projected gain (weighted by current ETF share)
        private decimal Tax2034()
        {
            var gain = ProjectedValue2034() - ProjectedInvested2034();
            if (gain <= 0) return 0;

            decimal etfVal = Store.Data.Holdings.Where(h => IsSold(h) && IsEtf(h)).Sum(h => ToEur(h));
            decimal totalVal = SellValueNow();
            decimal etfShare = totalVal > 0 ? etfVal / totalVal : 0;
            decimal blendedRate = etfShare * 0.41m + (1 - etfShare) * 0.33m;
            return gain * blendedRate;
        }

        private decimal ReturnTodayPct()
        {
            var invested = SellInvestedNow();
            if (invested == 0) return 0;
            var afterTax = SellValueNow() - TaxNow();
            return ((afterTax - invested) / invested) * 100m;
        }
        // Return % after tax — projected 2034
        private decimal Return2034Pct()
        {
            var invested = ProjectedInvested2034();
            if (invested == 0) return 0;
            var afterTax = ProjectedValue2034() - Tax2034();
            return ((afterTax - invested) / invested) * 100m;
        }

        private decimal AnnualizedReturn2034()
        {
            int months = MonthsToExit();
            if (months <= 0) return 0;
            decimal start = SellValueNow() - TaxNow();       // after-tax value today
            decimal monthly = ExitMonthly();
            decimal payout = ProjectedValue2034() - Tax2034();
            // Build cash flows: outflow today, monthly outflows, final inflow
            var flows = new List<double>();
            flows.Add(-(double)start);
            for (int m = 0; m < months - 1; m++)
                flows.Add(-(double)monthly);
            flows.Add(-(double)monthly + (double)payout);
            // Bisection solve for monthly IRR
            double lo = -0.99, hi = 1.0;
            for (int i = 0; i < 200; i++)
            {
                double mid = (lo + hi) / 2;
                double npv = 0;
                for (int j = 0; j < flows.Count; j++)
                    npv += flows[j] / Math.Pow(1 + mid, j);
                if (npv > 0) lo = mid; else hi = mid;
            }
            double monthlyIrr = (lo + hi) / 2;
            double annualIrr = Math.Pow(1 + monthlyIrr, 12) - 1;
            return (decimal)(annualIrr * 100);
        }
        private decimal ProjectedValueAt(decimal annualPct)
        {
            decimal value = SellValueNow();
            decimal monthly = ExitMonthly();
            decimal rate = (annualPct / 100m) / 12m;
            for (int m = 0; m < MonthsToExit(); m++)
            {
                value += monthly;
                value *= (1 + rate);
            }
            return value;
        }
        // After-tax version for a given rate
        private decimal ProjectedAfterTaxAt(decimal annualPct)
        {
            decimal gross = ProjectedValueAt(annualPct);
            decimal invested = ProjectedInvested2034();
            decimal gain = gross - invested;
            if (gain <= 0) return gross;
            decimal etfVal = Store.Data.Holdings.Where(h => IsSold(h) && IsEtf(h)).Sum(h => ToEur(h));
            decimal totalVal = SellValueNow();
            decimal etfShare = totalVal > 0 ? etfVal / totalVal : 0;
            decimal blendedRate = etfShare * 0.41m + (1 - etfShare) * 0.33m;
            return gross - (gain * blendedRate);
        }

        private decimal TargetFor(string ticker) => ticker switch
        {
            "NVDA" => 30m,
            "UNH" => 15m,
            "VUSA.AS" => 40m,
            "IUSA.AS" => 75m,
            "GC=F" => 1000m,    // Gold: 1 kg
            "SI=F" => 3000m,   // Silver: 3 kg
            "NWG.L" => 650m,
            "AZN" => 35m,
            "EVTL" => 300m,
            "SPCX" => 50m,
            _ => 0m       // no target set
        };

        // Grams held for a metal (oz × 31.1034768)
        private decimal MetalGrams(PortfolioHolding h)
        {
            var oz = Store.Data.Transactions
                .Where(t => t.HoldingId == h.Id)
                .Sum(t => t.QuantityReceived);
            return oz * 31.1034768m;
        }

        private string activeTab = "overall"; // "stocks" | "metals" | "overall"

        private bool IsStock(PortfolioHolding h) => !IsMetalTicker(h.TickerSymbol);

        private bool IsMetal(PortfolioHolding h) => IsMetalTicker(h.TickerSymbol);

        private IEnumerable<PortfolioHolding> FilteredHoldings() =>
                 activeTab == "overall" ? Store.Data.Holdings
               : activeTab == "stocks" ? Store.Data.Holdings.Where(IsStock)
               : Store.Data.Holdings.Where(IsMetal);

        private decimal FilteredInvestedEur() => FilteredHoldings().Sum(InvestedEur);

        private decimal FilteredCurrentEur() => FilteredHoldings().Sum(ToEur);

        private string FilteredPnlClass() => (FilteredCurrentEur() - FilteredInvestedEur()) < 0 ? "neg" : "pos";

        private string FilteredPnlText()
        {
            var invested = FilteredInvestedEur();
            if (invested == 0) return "-";
            var pnl = FilteredCurrentEur() - invested;
            var pct = (pnl / invested) * 100;
            var sign = pnl >= 0 ? "+" : "";
            return $"{sign}€{pnl:N2} ({sign}{pct:N1}%)";
        }

    }
}

using IrelandTaxTracker.Models;

namespace IrelandTaxTracker.Pages.Targets
{
    public partial class Targets
    {
        private List<TargetRow> _rows = new();

        private Timer? _timer;
        private decimal MoneyTarget = 5000m;

        protected override async Task OnInitializedAsync()

        {

            await Refresh();

            _timer = new Timer(async _ => await InvokeAsync(Refresh), null,

                TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        }

        private async Task Refresh()

        {

            var rows = new List<TargetRow>();

            foreach (var h in Store.Data.Holdings)

            {

                bool metal = IsMetalTicker(h.TickerSymbol);

                decimal have = metal ? MetalGrams(h) : ActualUnits(h);

                decimal tgt = h.TargetQuantity;

                decimal price = metal ? await MetalPriceEur(h) : await SharePriceNative(h);

                decimal monthly = h.MonthlyContribution;

                decimal sharesPerMonth = price > 0 && monthly > 0 ? monthly / price : 0;

                decimal remaining = Math.Max(0, tgt - have);

                decimal years = sharesPerMonth > 0 ? (remaining / sharesPerMonth) / 12m : 0;

                decimal pct = tgt > 0 ? Math.Min(100, (have / tgt) * 100) : 0;
                bool isMetal = IsMetalTicker(h.TickerSymbol);
                decimal investedEur = InvestedEur(h);
                decimal moneyTarget = isMetal ? 0 : MoneyTarget;   // €5k only for stocks
                decimal moneyPending = isMetal ? 0 : Math.Max(0, moneyTarget - investedEur);

                rows.Add(new TargetRow

                {

                    Name = h.Name,

                    Unit = metal ? "grams" : "shares",

                    Have = have,

                    Target = tgt,

                    Remaining = remaining,

                    Monthly = monthly,

                    PctComplete = pct,

                    YearsLeft = years,
                    InvestedEur = investedEur,
                    MoneyTarget = moneyTarget,
                    MoneyPending = moneyPending,

                });

            }

            _rows = rows;

            StateHasChanged();

        }

        private string LeadName() =>

            _rows.Where(r => r.Target > 0 && r.PctComplete < 100 && r.Monthly > 0)

                 .OrderBy(r => r.YearsLeft).FirstOrDefault()?.Name ?? "—";

        private string LaggardName() =>

            _rows.Where(r => r.Target > 0 && r.PctComplete < 100 && r.Monthly > 0)

                 .OrderByDescending(r => r.YearsLeft).FirstOrDefault()?.Name ?? "—";

        private void UpdateMonthly(PortfolioHolding h, object? value)

        {

            if (decimal.TryParse(value?.ToString(), out var m))

            {

                h.MonthlyContribution = m;

                Store.Save();

                _ = InvokeAsync(Refresh);

            }

        }

        private bool IsMetalTicker(string? t) => t == "GC=F" || t == "SI=F";

        private decimal ActualUnits(PortfolioHolding h) =>

            Store.Data.Transactions.Where(t => t.HoldingId == h.Id).Sum(t => t.QuantityReceived);

        private decimal MetalGrams(PortfolioHolding h) =>

            ActualUnits(h) * 31.1034768m;

        private async Task<decimal> SharePriceNative(PortfolioHolding h)

        {

            var p = await Prices.GetStockPriceAsync(h.TickerSymbol);

            return p ?? 0;

        }

        private async Task<decimal> MetalPriceEur(PortfolioHolding h)

        {

            var perGram = await Prices.GetMetalPricePerGramEurAsync(h.TickerSymbol);

            return perGram ?? 0;

        }

        private decimal InvestedEur(PortfolioHolding h)
        {
            // Sum of net amounts you paid, converted to EUR
            var nativeInvested = Store.Data.Transactions
                .Where(t => t.HoldingId == h.Id)
                .Sum(t => t.NetAmount);

            // Convert USD holdings to EUR; EUR holdings as-is
            return h.Currency == "USD"
                ? nativeInvested * Store.Data.UsdToEurRate
                : nativeInvested;
        }

        public void Dispose() => _timer?.Dispose();

        private class TargetRow

        {

            public string Name { get; set; } = "";

            public string Unit { get; set; } = "";

            public decimal Have { get; set; }

            public decimal Target { get; set; }

            public decimal Remaining { get; set; }

            public decimal Monthly { get; set; }

            public decimal PctComplete { get; set; }

            public decimal YearsLeft { get; set; }
            public decimal InvestedEur { get; set; }   // how much you've put in
            public decimal MoneyTarget { get; set; }   // €5,000 goal
            public decimal MoneyPending { get; set; }  // €5,000 - invested

        }

    }

}


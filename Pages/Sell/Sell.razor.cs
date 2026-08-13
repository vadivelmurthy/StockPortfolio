using IrelandTaxTracker.Models;

namespace IrelandTaxTracker.Pages.Sell
{
    public partial class Sell
    {
        private Dictionary<Guid, decimal> _unitsToSell = new();
        private Dictionary<Guid, decimal> _proceeds = new();
        private Dictionary<Guid, DateTime> _saleDates = new();
        private string? _confirmation;
        protected override void OnInitialized()
        {
            foreach (var h in Store.Data.Holdings)
            {
                _unitsToSell[h.Id] = 0;
                _proceeds[h.Id] = 0;
                _saleDates[h.Id] = DateTime.Today;
            }
        }
        private decimal ActualUnits(PortfolioHolding h) =>
            Store.Data.Transactions.Where(t => t.HoldingId == h.Id).Sum(t => t.QuantityReceived);
        private decimal InvestedNative(PortfolioHolding h) =>
            Store.Data.Transactions.Where(t => t.HoldingId == h.Id).Sum(t => t.Amount);
        private bool IsMetalTicker(string? ticker) => ticker == "GC=F" || ticker == "SI=F";
        private void RecordSale(PortfolioHolding h)
        {
            var units = _unitsToSell.GetValueOrDefault(h.Id, 0);
            var proceeds = _proceeds.GetValueOrDefault(h.Id, 0);
            var date = _saleDates.GetValueOrDefault(h.Id, DateTime.Today);
            if (units <= 0 || proceeds <= 0) return;
            // Calculate cost basis for CGT
            var totalUnits = ActualUnits(h);
            var totalInvested = InvestedNative(h);
            var costBasisNative = CalculateFifoCostBasis(h.TickerSymbol, units);
            var costBasisEur = h.Currency == "USD"
               ? costBasisNative * Store.Data.UsdToEurRate
               : costBasisNative;
            var proceedsEur = proceeds; // proceeds always entered in EUR
            var gain = proceedsEur - costBasisEur;
            // Save negative transaction (reduces units and invested amount)
            Store.Data.Transactions.Add(new Transaction
            {
                HoldingId = h.Id,
                Date = DateOnly.FromDateTime(date),
                Amount = -costBasisNative, // reduce invested by proportional cost
                QuantityReceived = -units   // reduce units held
            });
            // Auto-create CGT entry
            Store.Data.CgtSales.Add(new CgtSale
            {
                DateSold = DateOnly.FromDateTime(date),
                StockOrEtf = h.TickerSymbol,
                Quantity = units,
                CostBasis = Math.Round(costBasisEur, 2),
                SalePrice = Math.Round(proceedsEur, 2)
            });
            Store.Save();
            _confirmation = $"Sold {units:N4} of {h.Name} on {date:dd MMM yyyy} — " +
                           $"proceeds €{proceedsEur:N2}, cost basis €{costBasisEur:N2}, " +
                           $"CGT gain: €{gain:N2}. CGT entry auto-created for {date.Year}.";
            // Reset fields
            _unitsToSell[h.Id] = 0;
            _proceeds[h.Id] = 0;
        }

        private decimal CalculateFifoCostBasis(string ticker, decimal quantityToSell)
        {
            var holdingIds = Store.Data.Holdings
                .Where(h => h.TickerSymbol == ticker)
                .Select(h => h.Id)
                .ToList();
            var purchases = Store.Data.Transactions
                .Where(t => holdingIds.Contains(t.HoldingId) && t.QuantityReceived > 0)
                .OrderBy(t => t.Date)
                .Select(t => new
                {
                    Qty = t.QuantityReceived,
                    CostPerUnit = t.QuantityReceived > 0 ? t.NetAmount / t.QuantityReceived : 0
                })
                .ToList();
            decimal remaining = quantityToSell;
            decimal totalCostBasis = 0;
            foreach (var batch in purchases)
            {
                if (remaining <= 0) break;
                var takeFromBatch = Math.Min(batch.Qty, remaining);
                totalCostBasis += takeFromBatch * batch.CostPerUnit;
                remaining -= takeFromBatch;
            }
            return totalCostBasis;
        }
    }
}

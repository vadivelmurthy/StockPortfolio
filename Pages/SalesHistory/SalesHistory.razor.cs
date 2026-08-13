namespace IrelandTaxTracker.Pages.SalesHistory
{
    public partial class SalesHistory
    {
        public record StockSalesSummary(
            string StockOrEtf,
            decimal TotalQuantity,
            decimal TotalInvested,
            decimal TotalProceeds,
            decimal GainLoss,
            decimal ReturnPct);
        private List<StockSalesSummary> SalesGroupedByStock()
        {
            return Store.Data.CgtSales
                .GroupBy(s => s.StockOrEtf)
                .Select(g =>
                {
                    var invested = g.Sum(s => s.CostBasis);
                    var proceeds = g.Sum(s => s.SalePrice);
                    var gain = proceeds - invested;
                    var pct = invested != 0 ? (gain / invested) * 100 : 0;
                    return new StockSalesSummary(
                        g.Key,
                        g.Sum(s => s.Quantity),
                        invested,
                        proceeds,
                        gain,
                        pct);
                })
                .OrderByDescending(g => Math.Abs(g.GainLoss))
                .ToList();
        }
        private decimal OverallGainLoss() =>
            SalesGroupedByStock().Sum(g => g.GainLoss);
        private decimal OverallInvested() =>
            SalesGroupedByStock().Sum(g => g.TotalInvested);
        private decimal OverallProceeds() =>
            SalesGroupedByStock().Sum(g => g.TotalProceeds);
    }
}

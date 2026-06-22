namespace IrelandTaxTracker.Models;

public class CgtSale
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly DateSold { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string StockOrEtf { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal CostBasis { get; set; }
    public decimal SalePrice { get; set; }
    public decimal Gain => SalePrice - CostBasis;
}

public class EsppCycle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly VestDate { get; set; }
    public decimal MonthlyContribution { get; set; } = 307.68m;
    public int MonthsInCycle { get; set; } = 6;
    public decimal DiscountPercent { get; set; } = 0.10m;
    public string ReinvestmentSplit { get; set; } = "50% NatWest / 50% AstraZeneca";
    public bool Sold { get; set; }
    public DateOnly? SaleDate { get; set; }
    public decimal? SalePrice { get; set; }

    public decimal TotalContribution => MonthlyContribution * MonthsInCycle;
    public decimal MarketValue => DiscountPercent >= 1 ? 0 : TotalContribution / (1 - DiscountPercent);
    public decimal DiscountAmount => MarketValue - TotalContribution;
    public decimal? CgtGain => Sold && SalePrice.HasValue ? SalePrice.Value - MarketValue : null;
}

public class PortfolioHolding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public decimal Value { get; set; }
    public string Currency { get; set; } = "EUR"; // EUR or USD
    public string TickerSymbol { get; set; } = ""; // optional, e.g. "NVDA", "VUSA.L" - leave blank to skip live pricing
    public bool IsMetal { get; set; } = false; // true for Gold/Silver - changes Value to mean grams, not currency
    public decimal GramsHeld { get; set; } = 0; // only used when IsMetal = true
    public decimal SharesHeld { get; set; } = 0; // only used when IsMetal = false, for stocks/ETFs
    public decimal InvestedAmount { get; set; } = 0; // total you've put in (native currency), for P&L comparison
}

public class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HoldingId { get; set; }
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public decimal Amount { get; set; } // EUR/USD spent
    public decimal QuantityReceived { get; set; } = 0; // actual XAU/XAG received (metals only)
}
public class AppData
{
    public decimal UsdToEurRate { get; set; } = 0.93m;
    public decimal AnnualCgtAllowance { get; set; } = 1270m;
    public List<CgtSale> CgtSales { get; set; } = new();
    public List<EsppCycle> EsppCycles { get; set; } = new();
    public List<PortfolioHolding> Holdings { get; set; } = new();
    public List<Transaction> Transactions { get; set; } = new();
}

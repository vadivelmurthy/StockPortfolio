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
    public decimal TargetQuantity { get; set; } = 0;
    public decimal MonthlyContribution { get; set; } = 0;
}

public class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HoldingId { get; set; }
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public decimal Amount { get; set; } // total spent including fee
    public decimal Fee { get; set; } = 0; // Revolut fee/commission
    public decimal NetAmount => Amount - Fee; // actual investment excluding fee
    public decimal QuantityReceived { get; set; } = 0;
}

public class Dividend
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HoldingId { get; set; }
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public decimal WithholdingTax { get; set; } = 0;
}

public class HomeLoan
{
    public decimal OriginalAmount { get; set; } = 0;
    public decimal CurrentBalance { get; set; } = 0;
    public decimal InterestRate { get; set; } = 0; // annual % e.g. 3.5
    public int TermYears { get; set; } = 30;
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public decimal MonthlyPayment { get; set; } = 0;
    public string LenderName { get; set; } = "";
    public List<OverPayment> OverPayments { get; set; } = new();
}
public class OverPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public decimal Amount { get; set; }
    public string Notes { get; set; } = "";
}
public class AppData
{
    public decimal UsdToEurRate { get; set; } = 0.93m;
    public decimal GbpToEurRate { get; set; } = 1.17m;
    public decimal AnnualCgtAllowance { get; set; } = 1270m;
    public List<CgtSale> CgtSales { get; set; } = new();
    public List<EsppCycle> EsppCycles { get; set; } = new();
    public List<PortfolioHolding> Holdings { get; set; } = new();
    public List<Transaction> Transactions { get; set; } = new();
    public List<Dividend> Dividends { get; set; } = new();
    public HomeLoan? Loan { get; set; }
}

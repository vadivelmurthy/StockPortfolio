using IrelandTaxTracker.Models;
using System.Linq;

namespace IrelandTaxTracker.Pages.Dividends
{
    public partial class Dividends
    {
        private Guid _holdingId;
        private DateTime _date = DateTime.Today;
        private decimal _amount;
        private string _currency = "EUR";
        private decimal _withholdingTax;
        private string? _message;
        protected override void OnInitialized()
        {
            _holdingId = Store.Data.Holdings.FirstOrDefault()?.Id ?? Guid.Empty;
        }
        // Convert dividend to EUR using live rate
        private decimal ToEur(Dividend d) =>
           d.Currency == "USD" ? d.Amount * Store.Data.UsdToEurRate :
           d.Currency == "GBP" ? d.Amount * Store.Data.GbpToEurRate :
           d.Currency == "GBX" ? (d.Amount / 100m) * Store.Data.GbpToEurRate :
           d.Amount;
        private decimal ToEurWht(Dividend d) =>
            d.Currency == "USD" ? d.WithholdingTax * Store.Data.UsdToEurRate :
            d.Currency == "GBP" ? d.WithholdingTax * Store.Data.GbpToEurRate :
            d.Currency == "GBX" ? (d.WithholdingTax / 100m) * Store.Data.GbpToEurRate :
            d.WithholdingTax;
        private decimal TotalEur() => Store.Data.Dividends.Sum(ToEur);
        private decimal TotalWithholdingEur() => Store.Data.Dividends.Sum(ToEurWht);
        private decimal ThisYearEur() => Store.Data.Dividends
            .Where(d => d.Date.Year == DateTime.Today.Year)
            .Sum(ToEur);
        private void AddDividend()
        {
            if (_amount <= 0) return;
            Store.Data.Dividends.Add(new Dividend
            {
                HoldingId = _holdingId,
                Date = DateOnly.FromDateTime(_date),
                Amount = _amount,
                Currency = _currency,
                WithholdingTax = _withholdingTax
            });
            Store.Save();
            _message = $"Dividend of {_currency} {_amount:N2} logged successfully.";
            _amount = 0;
            _withholdingTax = 0;
        }
        private void RemoveDividend(Dividend d)
        {
            Store.Data.Dividends.Remove(d);
            Store.Save();
        }
    }
}


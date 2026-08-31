using IrelandTaxTracker.Models;

namespace IrelandTaxTracker.Pages.CarLoanRepayment
{
    public partial class CarLoanRepayment
    {
        private CarLoan Loan => Store.Data.CarLoanData ??= new CarLoan();
        private Dictionary<Guid, decimal> _extraAmounts = new();
        private Dictionary<Guid, string> _extraNotes = new();
        protected override void OnInitialized()
        {
            foreach (var loan in Loan.Loans)
            {
                _extraAmounts[loan.Id] = 0;
                _extraNotes[loan.Id] = "";
            }
        }
        private void Save() => Store.Save();
        private decimal CombinedRemaining() => Loan.Loans.Sum(l => l.RemainingBalance);
        private int PaymentsPerYear(CarLoanAccount loan) =>
            loan.PaymentFrequency == "Biweekly" ? 26 : 12;
        private decimal AnnualizedPayment(CarLoanAccount loan) =>
            loan.StandardPayment * PaymentsPerYear(loan);
        private decimal CombinedMonthlyEquivalent() =>
            Loan.Loans.Sum(AnnualizedPayment) / 12m;
        private int PaymentsToPayOff(CarLoanAccount loan) =>
            loan.StandardPayment <= 0 ? 0 : (int)Math.Ceiling(loan.RemainingBalance / loan.StandardPayment);
        private DateOnly EstimatedPayoffDate(CarLoanAccount loan)
        {
            var payments = PaymentsToPayOff(loan);
            var intervalDays = loan.PaymentFrequency == "Biweekly" ? 14 : 30;
            return loan.NextPaymentDate.AddDays(payments * intervalDays);
        }
        private void LogOverpayment(CarLoanAccount loan)
        {
            var amount = _extraAmounts.GetValueOrDefault(loan.Id, 0);
            if (amount <= 0) return;
            loan.Overpayments.Add(new CarLoanOverpayment
            {
                Amount = amount,
                Notes = _extraNotes.GetValueOrDefault(loan.Id, "")
            });
            loan.RemainingBalance -= amount;
            Store.Save();
            _extraAmounts[loan.Id] = 0;
            _extraNotes[loan.Id] = "";
        }
        private void RemoveOverpayment(CarLoanAccount loan, CarLoanOverpayment op)
        {
            loan.Overpayments.Remove(op);
            loan.RemainingBalance += op.Amount;
            Store.Save();
        }

        private void LogEmiPayment(CarLoanAccount loan)
        {
            loan.RemainingBalance -= loan.StandardPayment;
            loan.Overpayments.Add(new CarLoanOverpayment
            {
                Amount = loan.StandardPayment,
                Notes = "EMI payment"
            });
            loan.NextPaymentDate = loan.NextPaymentDate.AddDays(
                loan.PaymentFrequency == "Biweekly" ? 14 : 30);
            Store.Save();
        }

        // Estimate how much of the original "total repayable" (incl. interest) remains,

        // proportional to principal paid down. Simplified estimate, not a full amortization model.

        private decimal RemainingTotalRepayable(CarLoanAccount loan)
        {
            if (loan.IsFixedTotalRepayable)
                return loan.RemainingBalance; // already includes interest, no scaling needed
            if (loan.OriginalPrincipal <= 0) return loan.RemainingBalance;
            var fractionRemaining = loan.RemainingBalance / loan.OriginalPrincipal;
            return loan.TotalAmountRepayable * fractionRemaining;
        }

        private decimal InterestPaidOrSaved(CarLoanAccount loan)
        {
            if (loan.IsFixedTotalRepayable)
            {
                // For fixed HP: total paid off so far (principal + interest combined, can't split cleanly)
                return loan.TotalAmountRepayable - loan.RemainingBalance;
            }
            var originalInterest = loan.TotalAmountRepayable - loan.OriginalPrincipal;
            var remainingInterest = RemainingTotalRepayable(loan) - loan.RemainingBalance;
            return originalInterest - remainingInterest;
        }

        private decimal CombinedRemainingTotalRepayable() =>

            Loan.Loans.Sum(RemainingTotalRepayable);

    }
}

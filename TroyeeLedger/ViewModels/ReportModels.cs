using TroyeeLedger.Models.Entities;
using TroyeeLedger.Models.Enums;

namespace TroyeeLedger.ViewModels;

public class AccountBalanceRow
{
    public int AccountId { get; set; }
    public int? ParentId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public AccountType Type { get; set; }
    public bool IsGroup { get; set; }
    public bool IsCashOrBank { get; set; }
    public bool IsCurrent { get; set; }
    public int Level { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }

    public bool IsDebitNature => Type is AccountType.Asset or AccountType.Expense;
    public decimal Net => Debit - Credit;
    public decimal Natural => IsDebitNature ? Debit - Credit : Credit - Debit;
    public decimal NetDebit => Net > 0 ? Net : 0;
    public decimal NetCredit => Net < 0 ? -Net : 0;
}

public class TrialBalanceVm
{
    public DateTime AsOf { get; set; }
    public bool HideZero { get; set; } = true;
    public bool ShowGroups { get; set; } = true;
    public List<AccountBalanceRow> Rows { get; set; } = new();
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }
    public bool IsBalanced => TotalDebit == TotalCredit;
}

public class PlRow
{
    public int AccountId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public bool IsGroup { get; set; }
    public decimal Current { get; set; }
    public decimal Previous { get; set; }
    public decimal Change => Current - Previous;
    public double? ChangePct => Previous == 0 ? null : (double)((Current - Previous) / Math.Abs(Previous));
}

public class ProfitLossVm
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public DateTime PrevFrom { get; set; }
    public DateTime PrevTo { get; set; }
    public List<PlRow> Income { get; set; } = new();
    public List<PlRow> Expense { get; set; } = new();
    public decimal TotalIncome { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal PrevTotalIncome { get; set; }
    public decimal PrevTotalExpense { get; set; }
    public decimal NetProfit => TotalIncome - TotalExpense;
    public decimal PrevNetProfit => PrevTotalIncome - PrevTotalExpense;
}

public class BalanceSheetVm
{
    public DateTime AsOf { get; set; }
    public List<AccountBalanceRow> Assets { get; set; } = new();
    public List<AccountBalanceRow> Liabilities { get; set; } = new();
    public List<AccountBalanceRow> Equity { get; set; } = new();
    public decimal TotalAssets { get; set; }
    public decimal TotalLiabilities { get; set; }
    public decimal TotalEquity { get; set; }
    public decimal CurrentEarnings { get; set; }
    public decimal TotalEquityWithEarnings => TotalEquity + CurrentEarnings;
    public decimal Difference => TotalAssets - (TotalLiabilities + TotalEquityWithEarnings);
    public bool IsBalanced => Difference == 0;
}

public class CashFlowLine
{
    public CashFlowCategory Category { get; set; }
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";
    public decimal Amount { get; set; }
}

public class CashFlowVm
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal ClosingCash { get; set; }
    public List<CashFlowLine> Lines { get; set; } = new();
    public decimal Total(CashFlowCategory c) => Lines.Where(l => l.Category == c).Sum(l => l.Amount);
    public decimal NetChange => Lines.Sum(l => l.Amount);
    public decimal ComputedClosing => OpeningCash + NetChange;
}

public class StatementLine
{
    public DateTime Date { get; set; }
    public int VoucherId { get; set; }
    public string VoucherNo { get; set; } = "";
    public VoucherType VoucherType { get; set; }
    public string? Narration { get; set; }
    public string Counterparts { get; set; } = "";
    public string? AccountName { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Balance { get; set; }
    public bool IsReconciled { get; set; }
}

public class AccountStatementVm
{
    public ChartOfAccount? Account { get; set; }
    public Party? Party { get; set; }
    public string Title { get; set; } = "";
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public decimal Opening { get; set; }
    public List<StatementLine> Lines { get; set; } = new();
    public decimal TotalDebit => Lines.Sum(l => l.Debit);
    public decimal TotalCredit => Lines.Sum(l => l.Credit);
    public decimal Closing => Opening + TotalDebit - TotalCredit;
    public bool ShowAccountColumn { get; set; }
}

public class DayBookVm
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public VoucherType? Type { get; set; }
    public List<Voucher> Vouchers { get; set; } = new();
    public decimal TotalAmount => Vouchers.Sum(v => v.TotalAmount);
}

public class VatRow
{
    public string TaxName { get; set; } = "";
    public decimal RatePercent { get; set; }
    public TaxKind Kind { get; set; }
    public string AccountName { get; set; } = "";
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Net => Kind == TaxKind.Output ? Credit - Debit : Debit - Credit;
    public decimal ImpliedTaxableValue => RatePercent == 0 ? 0 : Math.Round(Net * 100m / RatePercent, 2);
}

public class VatReportVm
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public List<VatRow> Rows { get; set; } = new();
    public decimal TotalOutput => Rows.Where(r => r.Kind == TaxKind.Output).Sum(r => r.Net);
    public decimal TotalInput => Rows.Where(r => r.Kind == TaxKind.Input).Sum(r => r.Net);
    public decimal NetPayable => TotalOutput - TotalInput;
}

public class CostCenterAccountLine
{
    public string AccountCode { get; set; } = "";
    public string AccountName { get; set; } = "";
    public AccountType Type { get; set; }
    public decimal Amount { get; set; }
}

public class CostCenterRow
{
    public int CostCenterId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public decimal Net => Income - Expense;
    public List<CostCenterAccountLine> Lines { get; set; } = new();
}

public class CostCenterReportVm
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public List<CostCenterRow> Rows { get; set; } = new();
}

public class PartyBalanceRow
{
    public int PartyId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public PartyType Type { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Balance => Debit - Credit;
    public decimal CreditLimit { get; set; }
    public bool OverLimit => CreditLimit > 0 && Balance > CreditLimit;
}

public class PartyBalancesVm
{
    public DateTime AsOf { get; set; }
    public List<PartyBalanceRow> Rows { get; set; } = new();
    public decimal TotalReceivable => Rows.Where(r => r.Balance > 0).Sum(r => r.Balance);
    public decimal TotalPayable => Rows.Where(r => r.Balance < 0).Sum(r => -r.Balance);
}

public class MonthlyPoint
{
    public string Label { get; set; } = "";
    public DateTime MonthStart { get; set; }
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public decimal Net => Income - Expense;
}

public class BudgetVarianceRow
{
    public int AccountId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public AccountType Type { get; set; }
    public decimal Budget { get; set; }
    public decimal Actual { get; set; }
    public decimal ExpectedToDate { get; set; }
    public decimal Variance => Type == AccountType.Expense ? Budget - Actual : Actual - Budget;
    public double Utilization => Budget == 0 ? 0 : (double)(Actual / Budget);
    public string Status =>
        Budget == 0 ? "No budget" :
        Type == AccountType.Expense
            ? (Actual > Budget ? "Over budget" : Actual > ExpectedToDate * 1.1m ? "Ahead of plan" : "On track")
            : (Actual >= Budget ? "Target met" : Actual < ExpectedToDate * 0.9m ? "Behind plan" : "On track");
}

public class BudgetVarianceVm
{
    public Budget Budget { get; set; } = null!;
    public double ElapsedFraction { get; set; }
    public List<BudgetVarianceRow> Rows { get; set; } = new();
}

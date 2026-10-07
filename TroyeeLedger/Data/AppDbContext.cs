using Microsoft.EntityFrameworkCore;
using TroyeeLedger.Models.Entities;

namespace TroyeeLedger.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<ChartOfAccount> ChartOfAccounts => Set<ChartOfAccount>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<VoucherDetail> VoucherDetails => Set<VoucherDetail>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<FiscalYear> FiscalYears => Set<FiscalYear>();
    public DbSet<CostCenter> CostCenters => Set<CostCenter>();
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<TaxRate> TaxRates => Set<TaxRate>();
    public DbSet<CompanySetting> CompanySettings => Set<CompanySetting>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<BudgetLine> BudgetLines => Set<BudgetLine>();
    public DbSet<RecurringTemplate> RecurringTemplates => Set<RecurringTemplate>();
    public DbSet<RecurringTemplateLine> RecurringTemplateLines => Set<RecurringTemplateLine>();
    public DbSet<BankStatementLine> BankStatementLines => Set<BankStatementLine>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<AppUser>().HasIndex(u => u.Username).IsUnique();

        b.Entity<ChartOfAccount>(e =>
        {
            e.HasIndex(a => a.Code).IsUnique();
            e.HasOne(a => a.Parent).WithMany(a => a.Children).HasForeignKey(a => a.ParentId);
        });

        b.Entity<Voucher>(e =>
        {
            e.HasIndex(v => v.VoucherNo).IsUnique();
            e.HasIndex(v => v.VoucherDate);
            e.HasIndex(v => v.Status);
            e.HasIndex(v => v.ChainIndex);
            e.HasOne(v => v.CreatedBy).WithMany().HasForeignKey(v => v.CreatedById);
            e.HasOne(v => v.ApprovedBy).WithMany().HasForeignKey(v => v.ApprovedById);
            e.HasOne(v => v.ReversalOf).WithMany().HasForeignKey(v => v.ReversalOfId);
            e.HasMany(v => v.Details).WithOne(d => d.Voucher!).HasForeignKey(d => d.VoucherId);
        });

        b.Entity<LedgerEntry>(e =>
        {
            e.HasIndex(l => new { l.AccountId, l.EntryDate });
            e.HasIndex(l => l.VoucherId);
            e.HasOne(l => l.Voucher).WithMany().HasForeignKey(l => l.VoucherId);
            e.HasOne(l => l.VoucherDetail).WithMany().HasForeignKey(l => l.VoucherDetailId);
        });

        b.Entity<CostCenter>().HasIndex(c => c.Code).IsUnique();
        b.Entity<Party>().HasIndex(p => p.Code).IsUnique();
        b.Entity<Budget>().HasMany(x => x.Lines).WithOne(l => l.Budget!).HasForeignKey(l => l.BudgetId);
        b.Entity<RecurringTemplate>().HasMany(x => x.Lines).WithOne(l => l.RecurringTemplate!).HasForeignKey(l => l.RecurringTemplateId);
        b.Entity<BankStatementLine>().HasOne(x => x.BankAccount).WithMany().HasForeignKey(x => x.BankAccountId);
        b.Entity<AuditLog>().HasIndex(a => a.Timestamp);

        // SQL Server rejects multiple cascade paths, so every relationship is
        // RESTRICT except the "owned" line collections that belong to a header.
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
        {
            var dep = fk.DeclaringEntityType.ClrType;
            var prin = fk.PrincipalEntityType.ClrType;
            bool owned = (dep == typeof(VoucherDetail) && prin == typeof(Voucher))
                      || (dep == typeof(BudgetLine) && prin == typeof(Budget))
                      || (dep == typeof(RecurringTemplateLine) && prin == typeof(RecurringTemplate));
            fk.DeleteBehavior = owned ? DeleteBehavior.Cascade : DeleteBehavior.Restrict;
        }
    }
}

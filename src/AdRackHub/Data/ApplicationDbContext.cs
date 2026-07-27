using AdRackHub.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AdRackHub.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Models.Route> Routes => Set<Models.Route>();
    public DbSet<Stop> Stops => Set<Stop>();
    public DbSet<StopVisit> StopVisits => Set<StopVisit>();
    public DbSet<CustomerRoute> CustomerRoutes => Set<CustomerRoute>();
    public DbSet<CustomerRouteStop> CustomerRouteStops => Set<CustomerRouteStop>();
    public DbSet<CustomerContract> CustomerContracts => Set<CustomerContract>();
    public DbSet<CustomerContractRoute> CustomerContractRoutes => Set<CustomerContractRoute>();
    public DbSet<BillingRun> BillingRuns => Set<BillingRun>();
    public DbSet<BillingRunInvoice> BillingRunInvoices => Set<BillingRunInvoice>();
    public DbSet<BillingRunInvoiceLine> BillingRunInvoiceLines => Set<BillingRunInvoiceLine>();
    public DbSet<CustomerBrochureScan> CustomerBrochureScans => Set<CustomerBrochureScan>();
    public DbSet<CustomerBrochureInventory> CustomerBrochureInventories => Set<CustomerBrochureInventory>();
    public DbSet<CustomerNote> CustomerNotes => Set<CustomerNote>();
    public DbSet<CustomerTask> CustomerTasks => Set<CustomerTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Customer>(e =>
        {
            e.HasIndex(c => c.CustomerName);
            e.Property(c => c.Status).HasConversion<string>();
            e.Property(c => c.Type).HasConversion<string>();
            e.HasOne(c => c.AccountManager)
                .WithMany()
                .HasForeignKey(c => c.AccountManagerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Contact>(e =>
        {
            e.Property(c => c.Role).HasConversion<string>();
            e.HasOne(c => c.Customer)
                .WithMany(c => c.Contacts)
                .HasForeignKey(c => c.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomerBrochureScan>(e =>
        {
            e.HasIndex(s => new { s.CustomerId, s.UploadedAt });
            e.HasOne(s => s.Customer)
                .WithMany(c => c.BrochureScans)
                .HasForeignKey(s => s.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomerBrochureInventory>(e =>
        {
            e.HasIndex(i => new { i.CustomerId, i.InventoryDate });
            e.HasOne(i => i.Customer)
                .WithMany(c => c.BrochureInventories)
                .HasForeignKey(i => i.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomerNote>(e =>
        {
            e.Property(n => n.Kind).HasConversion<string>().HasMaxLength(50);
            e.HasIndex(n => new { n.CustomerId, n.CreatedAt });
            e.HasOne(n => n.Customer)
                .WithMany(c => c.Notes)
                .HasForeignKey(n => n.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomerTask>(e =>
        {
            e.Property(t => t.Status).HasConversion<string>().HasMaxLength(50);
            e.HasIndex(t => new { t.CustomerId, t.Status, t.DueDate });
            e.HasOne(t => t.Customer)
                .WithMany(c => c.Tasks)
                .HasForeignKey(t => t.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Models.Route>(e =>
        {
            e.Property(r => r.Status).HasConversion<string>();
            e.Property(r => r.BillingFrequency).HasConversion<string>();
            e.Property(r => r.Price).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Stop>(e =>
        {
            e.Property(s => s.StopType).HasConversion<string>();
            e.Property(s => s.Status).HasConversion<string>();
            e.HasIndex(s => new { s.RouteId, s.StepNumber });
            e.HasOne(s => s.Route)
                .WithMany(r => r.Stops)
                .HasForeignKey(s => s.RouteId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StopVisit>(e =>
        {
            e.HasIndex(v => new { v.StopId, v.VisitedAt });
            e.HasOne(v => v.Stop)
                .WithMany(s => s.Visits)
                .HasForeignKey(v => v.StopId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomerRoute>(e =>
        {
            e.HasIndex(cr => new { cr.CustomerId, cr.RouteId }).IsUnique();
            e.Property(cr => cr.Status).HasConversion<string>();
            e.Property(cr => cr.BillingTerm).HasConversion<string>();
            e.Property(cr => cr.RatePerMonth).HasPrecision(18, 2);
            e.HasOne(cr => cr.Customer)
                .WithMany(c => c.CustomerRoutes)
                .HasForeignKey(cr => cr.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(cr => cr.Route)
                .WithMany(r => r.CustomerRoutes)
                .HasForeignKey(cr => cr.RouteId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CustomerRouteStop>(e =>
        {
            e.HasIndex(crs => new { crs.CustomerRouteId, crs.StopId }).IsUnique();
            e.HasOne(crs => crs.CustomerRoute)
                .WithMany(cr => cr.CustomerRouteStops)
                .HasForeignKey(crs => crs.CustomerRouteId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(crs => crs.Stop)
                .WithMany(s => s.CustomerRouteStops)
                .HasForeignKey(crs => crs.StopId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CustomerContract>(e =>
        {
            e.ToTable("CustomerBillings");
            e.Property(b => b.Term).HasConversion<string>();
            e.Property(b => b.ContractName).HasColumnName("BillName");
            e.HasIndex(b => new { b.CustomerId, b.ContractName }).IsUnique();
            e.HasOne(b => b.Customer)
                .WithMany(c => c.Contracts)
                .HasForeignKey(b => b.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomerContractRoute>(e =>
        {
            e.ToTable("CustomerBillingRoutes");
            e.Property(cbr => cbr.CustomerContractId).HasColumnName("CustomerBillingId");
            e.HasIndex(cbr => new { cbr.CustomerContractId, cbr.RouteId }).IsUnique();
            e.HasOne(cbr => cbr.Contract)
                .WithMany(b => b.ContractRoutes)
                .HasForeignKey(cbr => cbr.CustomerContractId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(cbr => cbr.Route)
                .WithMany()
                .HasForeignKey(cbr => cbr.RouteId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillingRun>(e =>
        {
            e.HasIndex(r => new { r.Year, r.Month }).IsUnique();
            e.Property(r => r.Status).HasConversion<string>();
        });

        modelBuilder.Entity<BillingRunInvoice>(e =>
        {
            e.Property(i => i.TotalAmount).HasPrecision(18, 2);
            e.Property(i => i.Status).HasConversion<string>();
            e.HasIndex(i => new { i.BillingRunId, i.CustomerId }).IsUnique();
            e.HasOne(i => i.BillingRun)
                .WithMany(r => r.Invoices)
                .HasForeignKey(i => i.BillingRunId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Customer)
                .WithMany()
                .HasForeignKey(i => i.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillingRunInvoiceLine>(e =>
        {
            e.Property(l => l.Amount).HasPrecision(18, 2);
            e.Property(l => l.Term).HasConversion<string>();
            e.HasOne(l => l.BillingRunInvoice)
                .WithMany(i => i.Lines)
                .HasForeignKey(l => l.BillingRunInvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

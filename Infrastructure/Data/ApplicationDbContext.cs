// Infrastructure/Data/ApplicationDbContext.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EnterpriseAPI.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    private readonly ICurrentUserService _currentUserService;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentUserService currentUserService) 
        : base(options)
    {
        _currentUserService = currentUserService;
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all configurations from assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Global query filters (soft delete, multi-tenancy)
        modelBuilder.Entity<Customer>().HasQueryFilter(c => !c.IsDeleted);
        modelBuilder.Entity<Order>().HasQueryFilter(o => !o.IsDeleted);

        // Value conversions for enums
        var orderStatusConverter = new EnumToStringConverter<OrderStatus>();
        modelBuilder.Entity<Order>()
            .Property(o => o.Status)
            .HasConversion(orderStatusConverter);

        // Computed columns
        modelBuilder.Entity<OrderItem>()
            .Property(oi => oi.LineTotal)
            .HasComputedColumnSql("[Quantity] * [UnitPrice] * (1 - [Discount])", stored: true);

        // Indexes for performance
        modelBuilder.Entity<Order>()
            .HasIndex(o => new { o.CustomerId, o.OrderDate })
            .IncludeProperties(o => new { o.TotalAmount, o.Status });

        // Seed data
        SeedData(modelBuilder);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder
                .UseSqlServer(b => b
                    .EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorNumbersToAdd: null)
                    .CommandTimeout(30))
                .EnableSensitiveDataLogging(false)
                .EnableDetailedErrors(false)
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking); // Performance
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Auto-populate audit fields
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = DateTime.UtcNow;
                    entry.Entity.CreatedBy = _currentUserService.UserId ?? "system";
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    entry.Entity.UpdatedBy = _currentUserService.UserId ?? "system";
                    break;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    private void SeedData(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>().HasData(
            new Customer
            {
                Id = 1,
                CompanyName = "Acme Corporation",
                Email = "info@acme.com",
                VatNumber = "12345678901",
                CreditLimit = 50000,
                Status = CustomerStatus.Active,
                CreatedAt = DateTime.UtcNow
            }
        );
    }
}

// Infrastructure/Data/Configurations/OrderConfiguration.cs
public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", "dbo");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.OrderNumber)
            .IsRequired()
            .HasMaxLength(20);

        // Relationship configurations
        builder.HasOne(o => o.Customer)
            .WithMany(c => c.Orders)
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict); // Prevent cascade delete

        builder.HasMany(o => o.Items)
            .WithOne(oi => oi.Order)
            .HasForeignKey(oi => oi.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Performance indexes
        builder.HasIndex(o => o.OrderNumber).IsUnique();
        builder.HasIndex(o => o.OrderDate).IsDescending();
        builder.HasIndex(o => new { o.CustomerId, o.Status });
    }
}
/* DbContext OttimizzatoDbContext Ottimizzato */

// Domain/Entities/Customer.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EnterpriseAPI.Domain.Entities;

[Table("Customers", Schema = "dbo")]
[Index(nameof(Email), IsUnique = true)]
[Index(nameof(VatNumber), IsUnique = true)]
public class Customer : BaseEntity
{
    [MaxLength(200)]
    public required string CompanyName { get; set; }

    [MaxLength(100)]
    public required string Email { get; set; }

    [MaxLength(11)]
    public required string VatNumber { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(10)]
    public string? PostalCode { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(2)]
    public string Country { get; set; } = "IT";

    [Column(TypeName = "decimal(18,2)")]
    public decimal CreditLimit { get; set; }

    public CustomerStatus Status { get; set; }

    // Navigation Properties
    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    // Audit Fields (from BaseEntity)
    // CreatedAt, UpdatedAt, CreatedBy, UpdatedBy
}

[Table("Orders", Schema = "dbo")]
[Index(nameof(OrderDate), IsDescending = true)]
[Index(nameof(CustomerId), nameof(Status))]
public class Order : BaseEntity
{
    [MaxLength(20)]
    public required string OrderNumber { get; set; }

    public DateTime OrderDate { get; set; }

    [ForeignKey(nameof(Customer))]
    public int CustomerId { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal VatAmount { get; set; }

    public OrderStatus Status { get; set; }

    [MaxLength(50)]
    public string? As400OrderId { get; set; } // Legacy system reference

    [MaxLength(50)]
    public string? SapOrderId { get; set; } // SAP reference

    public DateTime? ShippingDate { get; set; }
    public DateTime? DeliveryDate { get; set; }

    // Navigation
    public virtual Customer Customer { get; set; } = null!;
    public virtual ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}

[Table("OrderItems", Schema = "dbo")]
public class OrderItem : BaseEntity
{
    [ForeignKey(nameof(Order))]
    public int OrderId { get; set; }

    [ForeignKey(nameof(Product))]
    public int ProductId { get; set; }

    public int Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Discount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal LineTotal { get; set; }

    // Navigation
    public virtual Order Order { get; set; } = null!;
    public virtual Product Product { get; set; } = null!;
}

// Domain/Entities/BaseEntity.cs
public abstract class BaseEntity
{
    [Key]
    public int Id { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    [MaxLength(100)]
    public string CreatedBy { get; set; } = "system";

    [MaxLength(100)]
    public string? UpdatedBy { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; } // Concurrency token
}

// Domain/Enums/OrderStatus.cs
public enum OrderStatus
{
    Draft = 0,
    Pending = 1,
    Confirmed = 2,
    Processing = 3,
    Shipped = 4,
    Delivered = 5,
    Cancelled = 6,
    OnHold = 7
}

public enum CustomerStatus
{
    Active = 1,
    Inactive = 2,
    Suspended = 3,
    Blacklisted = 4
}
/*  Domain Models con EF Core */

-- Database/StoredProcedures/usp_GetCustomerOrdersSummary.sql
CREATE OR ALTER PROCEDURE dbo.usp_GetCustomerOrdersSummary
    @CustomerId INT,
    @FromDate DATE = NULL,
    @ToDate DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Parametri di default
    SET @FromDate = ISNULL(@FromDate, DATEADD(MONTH, -12, GETDATE()));
    SET @ToDate = ISNULL(@ToDate, GETDATE());
    
    -- Statistiche aggregate con performance ottimizzate
    SELECT 
        c.Id AS CustomerId,
        c.CompanyName,
        c.CreditLimit,
        COUNT(DISTINCT o.Id) AS TotalOrders,
        SUM(o.TotalAmount) AS TotalRevenue,
        AVG(o.TotalAmount) AS AvgOrderValue,
        MAX(o.OrderDate) AS LastOrderDate,
        SUM(CASE WHEN o.Status = 5 THEN 1 ELSE 0 END) AS DeliveredOrders,
        SUM(CASE WHEN o.Status = 6 THEN 1 ELSE 0 END) AS CancelledOrders,
        -- Calcolo credit utilization
        (
            SELECT ISNULL(SUM(TotalAmount), 0)
            FROM dbo.Orders
            WHERE CustomerId = @CustomerId
              AND Status IN (1, 2, 3) -- Pending, Confirmed, Processing
        ) AS OutstandingAmount,
        c.CreditLimit - (
            SELECT ISNULL(SUM(TotalAmount), 0)
            FROM dbo.Orders
            WHERE CustomerId = @CustomerId
              AND Status IN (1, 2, 3)
        ) AS AvailableCredit
    FROM 
        dbo.Customers c
        LEFT JOIN dbo.Orders o ON c.Id = o.CustomerId
            AND o.OrderDate BETWEEN @FromDate AND @ToDate
    WHERE 
        c.Id = @CustomerId
    GROUP BY 
        c.Id, c.CompanyName, c.CreditLimit
    
    OPTION (RECOMPILE); -- Per parametri variabili
END;
GO

-- Database/StoredProcedures/usp_GetOrderDetailsOptimized.sql
CREATE OR ALTER PROCEDURE dbo.usp_GetOrderDetailsOptimized
    @OrderId INT
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Order header
    SELECT 
        o.Id,
        o.OrderNumber,
        o.OrderDate,
        o.TotalAmount,
        o.VatAmount,
        o.Status,
        o.ShippingDate,
        o.DeliveryDate,
        o.As400OrderId,
        o.SapOrderId,
        -- Customer info (evita JOIN con subquery)
        c.Id AS CustomerId,
        c.CompanyName AS CustomerName,
        c.Email AS CustomerEmail,
        c.VatNumber AS CustomerVat
    FROM 
        dbo.Orders o
        INNER JOIN dbo.Customers c ON o.CustomerId = c.Id
    WHERE 
        o.Id = @OrderId;
    
    -- Order items con dettagli prodotto
    SELECT 
        oi.Id,
        oi.OrderId,
        oi.ProductId,
        oi.Quantity,
        oi.UnitPrice,
        oi.Discount,
        oi.LineTotal,
        p.ProductCode,
        p.ProductName,
        p.Category
    FROM 
        dbo.OrderItems oi
        INNER JOIN dbo.Products p ON oi.ProductId = p.Id
    WHERE 
        oi.OrderId = @OrderId
    ORDER BY 
        oi.Id;
END;
GO

-- Database/Views/vw_OrdersAnalytics.sql
CREATE OR ALTER VIEW dbo.vw_OrdersAnalytics
AS
SELECT 
    o.Id AS OrderId,
    o.OrderNumber,
    o.OrderDate,
    YEAR(o.OrderDate) AS OrderYear,
    MONTH(o.OrderDate) AS OrderMonth,
    DATEPART(QUARTER, o.OrderDate) AS OrderQuarter,
    o.CustomerId,
    c.CompanyName AS CustomerName,
    c.Country,
    o.TotalAmount,
    o.VatAmount,
    o.Status,
    CASE 
        WHEN o.Status = 5 THEN 'Completed'
        WHEN o.Status = 6 THEN 'Cancelled'
        WHEN o.Status IN (1,2,3,4) THEN 'In Progress'
        ELSE 'Other'
    END AS StatusCategory,
    COUNT(*) OVER (PARTITION BY o.CustomerId) AS CustomerOrderCount,
    SUM(o.TotalAmount) OVER (PARTITION BY o.CustomerId) AS CustomerLifetimeValue,
    DATEDIFF(DAY, o.OrderDate, o.DeliveryDate) AS DeliveryDays,
    -- Performance indicators
    CASE 
        WHEN DATEDIFF(DAY, o.OrderDate, o.DeliveryDate) <= 7 THEN 'Fast'
        WHEN DATEDIFF(DAY, o.OrderDate, o.DeliveryDate) <= 14 THEN 'Normal'
        ELSE 'Slow'
    END AS DeliverySpeed,
    o.CreatedAt,
    o.UpdatedAt
FROM 
    dbo.Orders o
    INNER JOIN dbo.Customers c ON o.CustomerId = c.Id
WHERE 
    o.IsDeleted = 0;
GO
# /* Stored Procedures per Performance Critiche */
# /*  Query T-SQL Ottimizzate */

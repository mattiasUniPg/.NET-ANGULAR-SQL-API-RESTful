-- Database/Indexes/IX_Orders_Performance.sql

-- Covering index per query dashboard
CREATE NONCLUSTERED INDEX IX_Orders_Dashboard
ON dbo.Orders (OrderDate DESC, Status)
INCLUDE (CustomerId, TotalAmount, OrderNumber)
WITH (ONLINE = ON, FILLFACTOR = 90);

-- Index per ricerche customer
CREATE NONCLUSTERED INDEX IX_Orders_Customer_Status
ON dbo.Orders (CustomerId, Status, OrderDate DESC)
INCLUDE (TotalAmount, VatAmount)
WITH (ONLINE = ON);

-- Index per reportistica
CREATE NONCLUSTERED COLUMNSTORE INDEX IX_Orders_Analytics
ON dbo.Orders (
    OrderDate, CustomerId, TotalAmount, VatAmount, Status
);

-- Index per ricerche full-text
CREATE FULLTEXT INDEX ON dbo.Orders (OrderNumber)
KEY INDEX PK_Orders
ON OrdersFullTextCatalog;

-- Statistiche aggiornate automaticamente
CREATE STATISTICS ST_Orders_CustomerDate 
ON dbo.Orders (CustomerId, OrderDate)
WITH FULLSCAN;
# /*  Indici Ottimizzati per Query Pattern */

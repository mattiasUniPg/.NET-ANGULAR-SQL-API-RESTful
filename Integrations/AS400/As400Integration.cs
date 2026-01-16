// Integrations/AS400/As400Integration.cs
using IBM.Data.DB2.iSeries;

public interface IAs400Integration
{
    bool IsEnabled { get; }
    Task<string> CreateOrderAsync(Order order);
    Task<As400Order?> GetOrderAsync(string as400OrderId);
    Task UpdateOrderStatusAsync(string as400OrderId, string status);
}

public class As400Integration : IAs400Integration
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<As400Integration> _logger;
    private readonly string _connectionString;

    public bool IsEnabled => _configuration.GetValue<bool>("AS400:Enabled");

    public As400Integration(
        IConfiguration configuration,
        ILogger<As400Integration> logger)
    {
        _configuration = configuration;
        _logger = logger;

        var server = configuration["AS400:Server"];
        var userId = configuration["AS400:UserId"];
        var password = configuration["AS400:Password"];
        var defaultLibrary = configuration["AS400:DefaultLibrary"];

        _connectionString = $"DataSource={server};UserID={userId};Password={password};DefaultCollection={defaultLibrary};";
    }

    public async Task<string> CreateOrderAsync(Order order)
    {
        if (!IsEnabled)
        {
            _logger.LogWarning("AS/400 integration is disabled");
            return string.Empty;
        }

        using var connection = new iDB2Connection(_connectionString);
        await connection.OpenAsync();

        using var transaction = connection.BeginTransaction();
        
        try
        {
            // Insert nel file ordini AS/400
            var insertOrderCmd = new iDB2Command(
                @"INSERT INTO ORDERS_FILE 
                  (ORDNUM, ORDDTE, CUSTID, ORDAMT, ORDSTS) 
                  VALUES (@OrderNum, @OrderDate, @CustomerId, @Amount, @Status)",
                connection,
                transaction
            );

            var as400OrderId = $"AS{order.Id:D8}";

            insertOrderCmd.Parameters.AddWithValue("@OrderNum", as400OrderId);
            insertOrderCmd.Parameters.AddWithValue("@OrderDate", order.OrderDate.ToString("yyyyMMdd"));
            insertOrderCmd.Parameters.AddWithValue("@CustomerId", order.CustomerId);
            insertOrderCmd.Parameters.AddWithValue("@Amount", order.TotalAmount);
            insertOrderCmd.Parameters.AddWithValue("@Status", "PEND");

            await insertOrderCmd.ExecuteNonQueryAsync();

            // Insert righe ordine
            foreach (var item in order.Items)
            {
                var insertItemCmd = new iDB2Command(
                    @"INSERT INTO ORDER_ITEMS_FILE 
                      (ORDNUM, PRDID, QTY, PRICE) 
                      VALUES (@OrderNum, @ProductId, @Quantity, @Price)",
                    connection,
                    transaction
                );

                insertItemCmd.Parameters.AddWithValue("@OrderNum", as400OrderId);
                insertItemCmd.Parameters.AddWithValue("@ProductId", item.ProductId);
                insertItemCmd.Parameters.AddWithValue("@Quantity", item.Quantity);
                insertItemCmd.Parameters.AddWithValue("@Price", item.UnitPrice);

                await insertItemCmd.ExecuteNonQueryAsync();
            }

            // Call RPG program per validazione (opzionale)
            var callCmd = new iDB2Command(
                "CALL ORDVAL_PGM(@OrderNum, @ResultCode, @Message)",
                connection,
                transaction
            );

            callCmd.Parameters.Add("@OrderNum", iDB2DbType.iDB2Char, 10);
            callCmd.Parameters["@OrderNum"].Value = as400OrderId;
            
            callCmd.Parameters.Add("@ResultCode", iDB2DbType.iDB2Integer);
            callCmd.Parameters["@ResultCode"].Direction = ParameterDirection.Output;
            
            callCmd.Parameters.Add("@Message", iDB2DbType.iDB2VarChar, 100);
            callCmd.Parameters["@Message"].Direction = ParameterDirection.Output;

            await callCmd.ExecuteNonQueryAsync();

            var resultCode = (int)callCmd.Parameters["@ResultCode"].Value;
            
            if (resultCode != 0)
            {
                var message = (string)callCmd.Parameters["@Message"].Value;
                throw new As400Exception($"AS/400 validation failed: {message}");
            }

            await transaction.CommitAsync();

            _logger.LogInformation(
                "Order {OrderId} synchronized to AS/400 as {As400OrderId}",
                order.Id,
                as400OrderId
            );

            return as400OrderId;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error synchronizing order {OrderId} to AS/400", order.Id);
            throw;
        }
    }

    public async Task<As400Order?> GetOrderAsync(string as400OrderId)
    {
        using var connection = new iDB2Connection(_connectionString);
        await connection.OpenAsync();

        var cmd = new iDB2Command(
            @"SELECT ORDNUM, ORDDTE, CUSTID, ORDAMT, ORDSTS 
              FROM ORDERS_FILE 
              WHERE ORDNUM = @OrderNum",
            connection
        );

        cmd.Parameters.AddWithValue("@OrderNum", as400OrderId);

        using var reader = await cmd.ExecuteReaderAsync();
        
        if (await reader.ReadAsync())
        {
            return new As400Order
            {
                OrderNumber = reader.GetString(0).Trim(),
                OrderDate = DateTime.ParseExact(reader.GetString(1), "yyyyMMdd", null),
                CustomerId = reader.GetInt32(2),
                Amount = reader.GetDecimal(3),
                Status = reader.GetString(4).Trim()
            };
        }

        return null;
    }

    public async Task UpdateOrderStatusAsync(string as400OrderId, string status)
    {
        using var connection = new iDB2Connection(_connectionString);
        await connection.OpenAsync();

        var cmd = new iDB2Command(
            @"UPDATE ORDERS_FILE 
              SET ORDSTS = @Status, UPDDTE = @UpdateDate 
              WHERE ORDNUM = @OrderNum",
            connection
        );

        cmd.Parameters.AddWithValue("@Status", status);
        cmd.Parameters.AddWithValue("@UpdateDate", DateTime.Now.ToString("yyyyMMdd"));
        cmd.Parameters.AddWithValue("@OrderNum", as400OrderId);

        await cmd.ExecuteNonQueryAsync();
    }
}
/* Integrazione AS/400 IBM i */
/* Integrazione AS/400 e SAP */

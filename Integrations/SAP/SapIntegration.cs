// Integrations/SAP/SapIntegration.cs
using SAPConnector; // Libreria SAP .NET Connector

public interface ISapIntegration
{
    bool IsEnabled { get; }
    Task<string> CreateSalesOrderAsync(Order order);
    Task<SapOrderStatus> GetOrderStatusAsync(string sapOrderId);
    Task<List<SapProduct>> GetProductsAsync(string materialGroup);
}

public class SapIntegration : ISapIntegration
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SapIntegration> _logger;
    private RfcDestination _destination;

    public bool IsEnabled => _configuration.GetValue<bool>("SAP:Enabled");

    public SapIntegration(
        IConfiguration configuration,
        ILogger<SapIntegration> logger)
    {
        _configuration = configuration;
        _logger = logger;

        InitializeDestination();
    }

    private void InitializeDestination()
    {
        var sapConfig = new SapDestinationConfig
        {
            Name = "SAP_ECC",
            AppServerHost = _configuration["SAP:Host"],
            SystemNumber = _configuration["SAP:SystemNumber"],
            Client = _configuration["SAP:Client"],
            User = _configuration["SAP:User"],
            Password = _configuration["SAP:Password"],
            Language = "EN"
        };

        RfcConfigParameters parameters = new RfcConfigParameters();
        parameters.Add(RfcConfigParameters.AppServerHost, sapConfig.AppServerHost);
        parameters.Add(RfcConfigParameters.SystemNumber, sapConfig.SystemNumber);
        parameters.Add(RfcConfigParameters.Client, sapConfig.Client);
        parameters.Add(RfcConfigParameters.User, sapConfig.User);
        parameters.Add(RfcConfigParameters.Password, sapConfig.Password);
        parameters.Add(RfcConfigParameters.Language, sapConfig.Language);

        _destination = RfcDestinationManager.GetDestination(parameters);
    }

    public async Task<string> CreateSalesOrderAsync(Order order)
    {
        if (!IsEnabled)
        {
            _logger.LogWarning("SAP integration is disabled");
            return string.Empty;
        }

        return await Task.Run(() =>
        {
            try
            {
                // Chiama BAPI SAP per creazione ordine
                RfcRepository repository = _destination.Repository;
                IRfcFunction bapiFunction = repository.CreateFunction("BAPI_SALESORDER_CREATEFROMDAT2");

                // Header data
                IRfcStructure orderHeaderIn = bapiFunction.GetStructure("ORDER_HEADER_IN");
                orderHeaderIn.SetValue("DOC_TYPE", "TA"); // Order type
                orderHeaderIn.SetValue("SALES_ORG", "1000");
                orderHeaderIn.SetValue("DISTR_CHAN", "10");
                orderHeaderIn.SetValue("DIVISION", "00");
                orderHeaderIn.SetValue("PURCH_NO_C", order.OrderNumber);

                // Partner data (customer)
                IRfcTable partnerTable = bapiFunction.GetTable("ORDER_PARTNERS");
                partnerTable.Append();
                partnerTable.SetValue("PARTN_ROLE", "AG"); // Sold-to party
                partnerTable.SetValue("PARTN_NUMB", order.Customer.SapCustomerId);

                // Item data
                IRfcTable itemsTable = bapiFunction.GetTable("ORDER_ITEMS_IN");
                int itemNumber = 10;

                foreach (var item in order.Items)
                {
                    itemsTable.Append();
                    itemsTable.SetValue("ITM_NUMBER", itemNumber.ToString("D6"));
                    itemsTable.SetValue("MATERIAL", item.Product.SapMaterialId);
                    itemsTable.SetValue("TARGET_QTY", item.Quantity);
                    itemNumber += 10;
                }

                // Schedule lines
                IRfcTable schedulesTable = bapiFunction.GetTable("ORDER_SCHEDULES_IN");
                itemNumber = 10;

                foreach (var item in order.Items)
                {
                    schedulesTable.Append();
                    schedulesTable.SetValue("ITM_NUMBER", itemNumber.ToString("D6"));
                    schedulesTable.SetValue("REQ_QTY", item.Quantity);
                    itemNumber += 10;
                }

                // Invoke function
                bapiFunction.Invoke(_destination);

                // Check return messages
                IRfcTable returnTable = bapiFunction.GetTable("RETURN");
                bool hasErrors = false;
                StringBuilder errorMessages = new StringBuilder();

                foreach (IRfcStructure returnRow in returnTable)
                {
                    string msgType = returnRow.GetString("TYPE");
                    string message = returnRow.GetString("MESSAGE");

                    if (msgType == "E" || msgType == "A")
                    {
                        hasErrors = true;
                        errorMessages.AppendLine(message);
                    }
                }

                if (hasErrors)
                {
                    throw new SapException($"SAP BAPI errors: {errorMessages}");
                }

                // Get created order number
                string sapOrderId = bapiFunction.GetString("SALESDOCUMENT");

                if (string.IsNullOrEmpty(sapOrderId))
                {
                    throw new SapException("SAP did not return order number");
                }

                // Commit
                IRfcFunction commitFunction = repository.CreateFunction("BAPI_TRANSACTION_COMMIT");
                commitFunction.SetValue("WAIT", "X");
                commitFunction.Invoke(_destination);

                _logger.LogInformation(
                    "Order {OrderId} synchronized to SAP as {SapOrderId}",
                    order.Id,
                    sapOrderId
                );

                return sapOrderId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error synchronizing order {OrderId} to SAP", order.Id);
                throw;
            }
        });
    }

    public async Task<SapOrderStatus> GetOrderStatusAsync(string sapOrderId)
    {
        return await Task.Run(() =>
        {
            RfcRepository repository = _destination.Repository;
            IRfcFunction statusFunction = repository.CreateFunction("BAPI_SALESORDER_GETSTATUS");

            statusFunction.SetValue("SALESDOCUMENT", sapOrderId);
            statusFunction.Invoke(_destination);

            IRfcStructure statusData = statusFunction.GetStructure("STATUSINFO");

            return new SapOrderStatus
            {
                OrderNumber = sapOrderId,
                Status = statusData.GetString("STATUS"),
                DeliveryStatus = statusData.GetString("DLV_STAT"),
                BillingStatus = statusData.GetString("BILL_STAT")
            };
        });
    }

    public async Task<List<SapProduct>> GetProductsAsync(string materialGroup)
    {
        return await Task.Run(() =>
        {
            var products = new List<SapProduct>();

            RfcRepository repository = _destination.Repository;
            IRfcFunction productFunction = repository.CreateFunction("BAPI_MATERIAL_GETLIST");

            // Selection criteria
            IRfcTable maxRowsTable = productFunction.GetTable("MAXROWS");
            maxRowsTable.Append();
            maxRowsTable.SetValue("TABNAME", "MATNR");
            maxRowsTable.SetValue("ROWCOUNT", 1000);

            if (!string.IsNullOrEmpty(materialGroup))
            {
                IRfcTable selectionTable = productFunction.GetTable("MATNRSELECTION");
                selectionTable.Append();
                selectionTable.SetValue("SIGN", "I");
                selectionTable.SetValue("OPTION", "EQ");
                selectionTable.SetValue("MATNR_LOW", materialGroup);
            }

            productFunction.Invoke(_destination);

            IRfcTable materialsTable = productFunction.GetTable("MATNRLIST");

            foreach (IRfcStructure material in materialsTable)
            {
                products.Add(new SapProduct
                {
                    MaterialId = material.GetString("MATERIAL").TrimStart('0'),
                    Description = material.GetString("MATL_DESC"),
                    MaterialType = material.GetString("MATL_TYPE"),
                    MaterialGroup = material.GetString("MATL_GROUP")
                });
            }

            return products;
        });
    }
}
/* Integrazione SAP */

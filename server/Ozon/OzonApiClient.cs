using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Fulvero.Api.Ozon;

public class OzonApiClient(HttpClient httpClient, IOptions<OzonOptions> options)
{
    private readonly OzonOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private string? _clientIdOverride;
    private string? _apiKeyOverride;

    public void UseCredentials(string clientId, string apiKey)
    {
        _clientIdOverride = clientId;
        _apiKeyOverride = apiKey;
    }

    public async Task<OzonProductListResult> GetProductListAsync(int limit, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v3/product/list");
        AddAuthHeaders(request);
        request.Content = JsonContent.Create(new OzonProductListRequest(
            new OzonProductListFilter("ALL"),
            string.Empty,
            Math.Clamp(limit, 1, 1000)));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Ozon API returned {(int)response.StatusCode}: {content}",
                null,
                response.StatusCode);
        }

        var data = JsonSerializer.Deserialize<OzonProductListResponse>(content, JsonOptions)
            ?? throw new InvalidOperationException("Ozon API returned an empty response.");

        return data.Result;
    }

    public async Task<OzonStockListResult> GetStocksAsync(int limit, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v4/product/info/stocks");
        AddAuthHeaders(request);
        request.Content = JsonContent.Create(new OzonStockListRequest(
            new OzonProductListFilter("ALL"),
            string.Empty,
            Math.Clamp(limit, 1, 1000)));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Ozon API returned {(int)response.StatusCode}: {content}",
                null,
                response.StatusCode);
        }

        var data = JsonSerializer.Deserialize<OzonStockListResult>(content, JsonOptions)
            ?? throw new InvalidOperationException("Ozon API returned an empty response.");

        return data;
    }

    public async Task<IReadOnlyList<OzonProductSummary>> GetProductSummariesAsync(int limit, CancellationToken cancellationToken)
    {
        var list = await GetProductListAsync(limit, cancellationToken);
        var ids = list.Items.Select(item => item.ProductId).ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await GetProductInfoAsync(ids, cancellationToken);
    }

    public async Task<IReadOnlyList<OzonStockSummary>> GetStockSummariesAsync(int limit, CancellationToken cancellationToken)
    {
        var stocks = await GetStocksAsync(limit, cancellationToken);
        var productIds = stocks.Items.Select(item => item.ProductId).Distinct().ToArray();
        var details = await GetProductInfoAsync(productIds, cancellationToken);
        var detailsById = details.ToDictionary(item => item.ProductId);

        return stocks.Items.Select(item =>
        {
            detailsById.TryGetValue(item.ProductId, out var detail);
            var fbo = item.Stocks.FirstOrDefault(stock => stock.Type.Equals("fbo", StringComparison.OrdinalIgnoreCase));
            var fbs = item.Stocks.FirstOrDefault(stock => stock.Type.Equals("fbs", StringComparison.OrdinalIgnoreCase));
            var sku = fbo?.Sku ?? fbs?.Sku ?? detail?.Sku;

            return new OzonStockSummary(
                item.ProductId,
                item.OfferId,
                sku,
                detail?.Name ?? string.Empty,
                detail?.Price ?? 0,
                detail?.OldPrice ?? 0,
                detail?.MinPrice ?? 0,
                detail?.CurrencyCode ?? string.Empty,
                fbo?.Present ?? 0,
                fbs?.Present ?? 0,
                sku is null ? string.Empty : $"https://www.ozon.kz/product/{sku}/",
                detail?.ImageUrl ?? string.Empty);
        }).ToList();
    }

    public async Task<OzonPriceUpdateResult> UpdatePriceAsync(OzonPriceUpdateRequest request, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/product/import/prices");
        AddAuthHeaders(httpRequest);
        httpRequest.Content = JsonContent.Create(new OzonImportPricesRequest([
            new OzonImportPriceItem(
                request.ProductId,
                request.OfferId,
                request.Price.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                GetOptionalOzonPrice(request.OldPrice, request.Price),
                GetOptionalOzonPrice(request.MinPrice, request.Price),
                request.CurrencyCode)
        ]));

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Ozon API returned {(int)response.StatusCode}: {content}",
                null,
                response.StatusCode);
        }

        var data = JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
        var errors = GetOzonPriceImportErrors(data);
        if (!string.IsNullOrWhiteSpace(errors))
        {
            return new OzonPriceUpdateResult(false, errors, data);
        }

        return new OzonPriceUpdateResult(true, "Цена успешно обновлена в Ozon", data);
    }

    public async Task<OzonAnalyticsResult> GetAnalyticsAsync(DateOnly dateFrom, DateOnly dateTo, CancellationToken cancellationToken)
    {
        var finance = await GetFinanceTransactionsAsync(dateFrom, dateTo, cancellationToken);

        var productRows = finance.Operations
            .Where(operation => operation.Type == "orders" || operation.Items.Count > 0)
            .SelectMany(operation => operation.Items.DefaultIfEmpty().Select(item =>
            {
                return new OzonAnalyticsRow(
                    item?.Sku ?? 0,
                    string.Empty,
                    item?.Name ?? operation.OperationTypeName,
                    operation.OperationTypeName,
                    operation.Posting.PostingNumber,
                    operation.AccrualsForSale > 0 ? 1 : 0,
                    operation.AccrualsForSale,
                    operation.AccrualsForSale == 0
                        ? 0
                        : Math.Round(Math.Abs(operation.SaleCommission) / operation.AccrualsForSale * 100, 2),
                    Math.Abs(operation.SaleCommission),
                    operation.Amount,
                    "KZT",
                    operation.Services.Sum(service => Math.Abs(service.Price)));
            }))
            .OrderByDescending(row => row.Revenue)
            .ToList();

        var revenueTotal = productRows.Sum(row => row.Revenue);
        var commissionTotal = productRows.Sum(row => row.CommissionAmount);
        var payoutTotal = productRows.Sum(row => row.Payout);
        var logisticsTotal = productRows.Sum(row => row.LogisticsAmount);
        var orderedUnitsTotal = productRows.Count(row => row.Revenue > 0);

        var postings = await GetFboPostingsAsync(dateFrom, dateTo, cancellationToken);
        postings.AddRange(await GetFbsPostingsAsync(dateFrom, dateTo, cancellationToken));

        var dailySales = postings
            .Where(posting => posting.Status != "cancelled")
            .SelectMany(posting =>
            {
                var date = GetPostingDate(posting);
                return posting.Products
                    .Where(product => product.Quantity > 0)
                    .Select(product => new
                    {
                        Date = date,
                        product.Quantity,
                        Revenue = product.Price * product.Quantity
                    });
            })
            .GroupBy(item => item.Date)
            .Select(group => new OzonSalesDay(
                group.Key.ToString("yyyy-MM-dd"),
                group.Sum(item => item.Quantity),
                group.Sum(item => item.Revenue)))
            .OrderBy(row => row.Date)
            .ToList();

        var topProducts = postings
            .Where(posting => posting.Status != "cancelled")
            .SelectMany(posting => posting.Products)
            .Where(product => product.Quantity > 0)
            .GroupBy(product => product.Sku != 0 ? $"sku:{product.Sku}" : $"offer:{product.OfferId}")
            .Select(group =>
            {
                var first = group.First();
                return new OzonTopProductRow(
                    first.Sku,
                    first.OfferId,
                    first.Name,
                    group.Sum(product => product.Quantity),
                    group.Sum(product => product.Price * product.Quantity),
                    first.CurrencyCode);
            })
            .OrderByDescending(row => row.Quantity)
            .ThenByDescending(row => row.Revenue)
            .ToList();

        return new OzonAnalyticsResult(
            productRows,
            topProducts,
            dailySales,
            orderedUnitsTotal,
            revenueTotal,
            commissionTotal,
            payoutTotal,
            logisticsTotal,
            finance.Operations.Where(operation => operation.Type == "services").Sum(operation => operation.Amount),
            postings.Count(posting => posting.Status == "awaiting_packaging"),
            postings.Count(posting => posting.Status == "awaiting_deliver"),
            postings.Count(posting => posting.Status == "delivering"),
            postings.Count(posting => posting.Status == "delivered"),
            await GetAccountBalanceAsync(dateFrom, dateTo, cancellationToken),
            DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    private static DateOnly GetPostingDate(OzonPosting posting)
    {
        var value = !string.IsNullOrWhiteSpace(posting.CreatedAt) ? posting.CreatedAt : posting.InProcessAt;
        return DateTimeOffset.TryParse(value, out var parsed)
            ? DateOnly.FromDateTime(parsed.UtcDateTime)
            : DateOnly.FromDateTime(DateTime.UtcNow);
    }

    public async Task<IReadOnlyList<OzonSupplyOrderSummary>> GetSupplyOrdersAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var content = await SendSupplyOrderListRequestAsync("/v2/supply-order/list", dateFrom, dateTo, cancellationToken);

        var data = JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
        var orders = TryGetArray(data, "supply_orders")
            ?? TryGetArray(data, "result", "supply_orders")
            ?? TryGetArray(data, "result", "orders")
            ?? TryGetArray(data, "orders")
            ?? TryGetArray(data, "result")
            ?? [];

        var result = new List<OzonSupplyOrderSummary>();
        foreach (var order in orders)
        {
            var summary = MapSupplyOrder(order);
            if (string.IsNullOrWhiteSpace(summary.Id))
            {
                continue;
            }

            var detailed = await GetSupplyOrderDetailsAsync(summary.Id, cancellationToken);
            result.Add(MergeSupplyOrder(summary, detailed));
        }

        return result;
    }

    private async Task<OzonSupplyOrderSummary?> GetSupplyOrderDetailsAsync(
        string supplyOrderId,
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(supplyOrderId, out var numericSupplyOrderId))
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/supply-order/get");
        AddAuthHeaders(request);
        request.Content = JsonContent.Create(new OzonSupplyOrderGetRequest(numericSupplyOrderId));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var data = JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
        return MapSupplyOrder(UnwrapResult(data));
    }

    private static OzonSupplyOrderSummary MapSupplyOrder(JsonElement order)
    {
        order = UnwrapResult(order);
        return new OzonSupplyOrderSummary(
            GetString(order, "supply_order_id", "supply_order_number", "order_id", "number", "id"),
            GetString(order, "status", "state", "state_name"),
            BuildSupplyOrderWarehouseName(order),
            GetSupplyOrderShipmentDate(order),
            GetSupplyOrderCompletionDate(order),
            GetInt(order, "sku_count", "skus_count", "items_count", "itemsCount", "total_items_count", "total_quantity", "quantity"));
    }

    private static JsonElement UnwrapResult(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty("result", out var result)
            ? result
            : element;
    }

    private static OzonSupplyOrderSummary MergeSupplyOrder(
        OzonSupplyOrderSummary summary,
        OzonSupplyOrderSummary? detailed)
    {
        if (detailed is null)
        {
            return summary;
        }

        return summary with
        {
            Id = FirstFilled(detailed.Id, summary.Id),
            Status = FirstFilled(detailed.Status, summary.Status),
            WarehouseName = FirstFilled(detailed.WarehouseName, summary.WarehouseName),
            CreatedAt = FirstFilled(detailed.CreatedAt, summary.CreatedAt),
            UpdatedAt = FirstFilled(detailed.UpdatedAt, summary.UpdatedAt),
            ItemsCount = detailed.ItemsCount > 0 ? detailed.ItemsCount : summary.ItemsCount
        };
    }

    private async Task<string> SendSupplyOrderListRequestAsync(
        string path,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        AddAuthHeaders(request);
        request.Content = path == "/v2/supply-order/list"
            ? JsonContent.Create(new OzonSupplyOrderListV2Request(0, 100))
            : JsonContent.Create(new OzonSupplyOrderListRequest(
                $"{dateFrom:yyyy-MM-dd}T00:00:00Z",
                $"{dateTo:yyyy-MM-dd}T23:59:59Z",
                new OzonSupplyOrderFilter([]),
                100));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return content;
        }

        if (path == "/v2/supply-order/list"
            && (response.StatusCode == System.Net.HttpStatusCode.NotFound
                || response.StatusCode == System.Net.HttpStatusCode.BadRequest))
        {
            return await SendSupplyOrderListRequestAsync("/v1/supply-order/list", dateFrom, dateTo, cancellationToken);
        }

        if (path == "/v1/supply-order/list" && response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return await SendSupplyOrderListRequestAsync("/v1/fbo/supply-order/list", dateFrom, dateTo, cancellationToken);
        }

        throw new HttpRequestException(
            $"Ozon API returned {(int)response.StatusCode}: {content}",
            null,
            response.StatusCode);
    }

    private async Task<OzonAccountBalance> GetAccountBalanceAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken)
    {
        try
        {
            EnsureConfigured();

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/finance/cash-flow-statement/list");
            AddAuthHeaders(request);
            request.Content = JsonContent.Create(new OzonCashFlowStatementRequest(
                new OzonFinanceDateRange(
                    $"{dateFrom:yyyy-MM-dd}T00:00:00.000Z",
                    $"{dateTo:yyyy-MM-dd}T23:59:59.000Z"),
                1,
                100));

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new OzonAccountBalance(0, string.Empty);
            }

            var data = JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
            var resultBalance = GetDecimal(data, "balance", "amount", "current_balance", "end_balance", "end_balance_amount");
            if (resultBalance != 0)
            {
                return new OzonAccountBalance(resultBalance, GetString(data, "currency_code", "currency"));
            }

            if (data.ValueKind == JsonValueKind.Object
                && data.TryGetProperty("result", out var result)
                && result.ValueKind == JsonValueKind.Object)
            {
                var directBalance = GetDecimal(result, "balance", "amount", "current_balance", "end_balance", "end_balance_amount");
                if (directBalance != 0)
                {
                    return new OzonAccountBalance(directBalance, GetString(result, "currency_code", "currency"));
                }
            }

            var flows = TryGetArray(data, "result", "cash_flows") ?? [];
            var lastFlow = flows.LastOrDefault();
            if (lastFlow.ValueKind == JsonValueKind.Undefined)
            {
                return await GetFinanceTransactionTotalsBalanceAsync(dateFrom, dateTo, cancellationToken);
            }

            var details = TryGetArray(lastFlow, "details") ?? [];
            var lastDetail = details.LastOrDefault();
            var source = lastDetail.ValueKind == JsonValueKind.Undefined ? lastFlow : lastDetail;
            var currencyCode = GetString(source, "currency_code", "currency", "currencyCode");
            if (string.IsNullOrWhiteSpace(currencyCode))
            {
                currencyCode = GetString(lastFlow, "currency_code", "currency", "currencyCode");
            }

            var balance = GetFirstDecimal(source, "end_balance_amount", "end_balance", "balance", "amount", "outgoing_balance", "closing_balance");
            return balance == 0
                ? await GetFinanceTransactionTotalsBalanceAsync(dateFrom, dateTo, cancellationToken)
                : new OzonAccountBalance(balance, currencyCode);
        }
        catch
        {
            return new OzonAccountBalance(0, string.Empty);
        }
    }

    private async Task<OzonAccountBalance> GetFinanceTransactionTotalsBalanceAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/v3/finance/transaction/totals");
            AddAuthHeaders(request);
            request.Content = JsonContent.Create(new OzonFinanceTransactionTotalsRequest(
                new OzonFinanceDateRange(
                    $"{dateFrom:yyyy-MM-dd}T00:00:00.000Z",
                    $"{dateTo:yyyy-MM-dd}T23:59:59.000Z"),
                string.Empty,
                "all"));

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new OzonAccountBalance(0, string.Empty);
            }

            var data = JsonSerializer.Deserialize<JsonElement>(content, JsonOptions);
            if (data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("result", out var result)
                || result.ValueKind != JsonValueKind.Object)
            {
                return new OzonAccountBalance(0, string.Empty);
            }

            var total = GetDecimal(result, "accruals_for_sale")
                + GetDecimal(result, "compensation_amount")
                + GetDecimal(result, "money_transfer")
                + GetDecimal(result, "others_amount")
                + GetDecimal(result, "processing_and_delivery")
                + GetDecimal(result, "refunds_and_cancellations")
                + GetDecimal(result, "sale_commission")
                + GetDecimal(result, "services_amount");

            return new OzonAccountBalance(total, string.Empty);
        }
        catch
        {
            return new OzonAccountBalance(0, string.Empty);
        }
    }

    private async Task<OzonFinanceTransactionResult> GetFinanceTransactionsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v3/finance/transaction/list");
        AddAuthHeaders(request);
        request.Content = JsonContent.Create(new OzonFinanceTransactionRequest(
            new OzonFinanceFilter(
                new OzonFinanceDateRange(
                    $"{dateFrom:yyyy-MM-dd}T00:00:00.000Z",
                    $"{dateTo:yyyy-MM-dd}T23:59:59.000Z"),
                [],
                string.Empty,
                "all"),
            1,
            1000));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Ozon API returned {(int)response.StatusCode}: {content}",
                null,
                response.StatusCode);
        }

        var data = JsonSerializer.Deserialize<OzonFinanceTransactionResponse>(content, JsonOptions)
            ?? throw new InvalidOperationException("Ozon API returned an empty response.");

        return data.Result;
    }

    private async Task<List<OzonPosting>> GetFboPostingsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken)
    {
        var response = await SendPostingRequestAsync(
            "/v2/posting/fbo/list",
            dateFrom,
            dateTo,
            cancellationToken);

        var data = JsonSerializer.Deserialize<OzonFboPostingListResponse>(response, JsonOptions)
            ?? throw new InvalidOperationException("Ozon API returned an empty response.");

        return data.Result.ToList();
    }

    private async Task<List<OzonPosting>> GetFbsPostingsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken)
    {
        var response = await SendPostingRequestAsync(
            "/v3/posting/fbs/list",
            dateFrom,
            dateTo,
            cancellationToken);

        var data = JsonSerializer.Deserialize<OzonPostingListResponse>(response, JsonOptions)
            ?? throw new InvalidOperationException("Ozon API returned an empty response.");

        return data.Result.Postings.ToList();
    }

    private async Task<string> SendPostingRequestAsync(
        string path,
        DateOnly dateFrom,
        DateOnly dateTo,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        AddAuthHeaders(request);
        request.Content = JsonContent.Create(new OzonPostingListRequest(
            "ASC",
            new OzonPostingFilter(
                $"{dateFrom:yyyy-MM-dd}T00:00:00Z",
                $"{dateTo:yyyy-MM-dd}T23:59:59Z",
                string.Empty),
            1000,
            0,
            new OzonPostingWith(true, true)));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Ozon API returned {(int)response.StatusCode}: {content}",
                null,
                response.StatusCode);
        }

        return content;
    }

    private async Task<IReadOnlyList<OzonProductSummary>> GetProductInfoAsync(
        IReadOnlyCollection<long> productIds,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v3/product/info/list");
        AddAuthHeaders(request);
        request.Content = JsonContent.Create(new OzonProductInfoListRequest(productIds));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Ozon API returned {(int)response.StatusCode}: {content}",
                null,
                response.StatusCode);
        }

        var data = JsonSerializer.Deserialize<OzonProductInfoListResponse>(content, JsonOptions)
            ?? throw new InvalidOperationException("Ozon API returned an empty response.");

        return data.Items.Select(item =>
        {
            var sku = item.Sku ?? item.Sources?.FirstOrDefault()?.Sku;
            return new OzonProductSummary(
                item.Id,
                item.OfferId,
                sku,
                item.Name,
                GetJsonDecimal(item.Price),
                GetJsonDecimal(item.OldPrice),
                GetJsonDecimal(item.MinPrice),
                item.CurrencyCode,
                item.Statuses?.StatusName ?? string.Empty,
                sku is null ? string.Empty : $"https://www.ozon.kz/product/{sku}/",
                (item.PrimaryImage ?? []).FirstOrDefault() ?? (item.Images ?? []).FirstOrDefault() ?? string.Empty);
        }).ToList();
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Ozon API credentials are not configured.");
        }
    }

    private string ClientId => string.IsNullOrWhiteSpace(_clientIdOverride) ? _options.ClientId : _clientIdOverride;

    private string ApiKey => string.IsNullOrWhiteSpace(_apiKeyOverride) ? _options.ApiKey : _apiKeyOverride;

    private void AddAuthHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("Client-Id", ClientId);
        request.Headers.Add("Api-Key", ApiKey);
    }

    private static string GetOptionalOzonPrice(decimal? value, decimal price)
    {
        if (value is null || value <= 0 || value <= price)
        {
            return "0";
        }

        return value.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string GetOzonPriceImportErrors(JsonElement data)
    {
        var messages = new List<string>();

        if (!data.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var item in result.EnumerateArray())
        {
            if (item.TryGetProperty("updated", out var updated)
                && updated.ValueKind is JsonValueKind.False)
            {
                messages.Add("Ozon не обновил цену товара.");
            }

            if (!item.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var error in errors.EnumerateArray())
            {
                var message = error.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString()
                    : null;
                var code = error.TryGetProperty("code", out var codeElement)
                    ? codeElement.GetString()
                    : null;

                if (!string.IsNullOrWhiteSpace(message))
                {
                    messages.Add(message);
                }
                else if (!string.IsNullOrWhiteSpace(code))
                {
                    messages.Add(code);
                }
            }
        }

        return string.Join(" ", messages.Distinct());
    }

    private static JsonElement.ArrayEnumerator? TryGetArray(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var part in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.Array ? current.EnumerateArray() : null;
    }

    private static string GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(name, out var value))
            {
                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString() ?? string.Empty,
                    JsonValueKind.Number => value.ToString(),
                    _ => string.Empty
                };
            }
        }

        return string.Empty;
    }

    private static string GetNestedString(JsonElement element, string parent, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(parent, out var nested)
            || nested.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        return GetString(nested, names);
    }

    private static string FirstFilled(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static string BuildSupplyOrderWarehouseName(JsonElement order)
    {
        var cluster = FirstFilled(
            GetString(order, "cluster_name", "cluster", "placement_cluster", "placement_cluster_name", "region_name"),
            GetNestedString(order, "cluster", "name"));
        var warehouse = FirstFilled(
            GetString(order, "warehouse_name", "supply_warehouse_name", "delivery_method_name", "warehouse"),
            GetNestedString(order, "warehouse", "name"),
            GetNestedString(order, "supply_warehouse", "name"),
            GetNestedString(order, "seller_warehouse", "name"),
            GetNestedString(order, "delivery_method", "name"));

        return string.Join(" / ", new[] { cluster, warehouse }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string GetSupplyOrderShipmentDate(JsonElement order)
    {
        return FirstFilled(
            GetNestedString(order, "timeslot", "from", "date_from", "start_at"),
            GetNestedString(order, "local_timeslot", "from", "date_from", "start_at"),
            GetString(order, "shipment_date", "shipping_date", "delivery_date", "created_at", "createdAt", "created_date"));
    }

    private static string GetSupplyOrderCompletionDate(JsonElement order)
    {
        return FirstFilled(
            GetString(order, "completed_at", "completedAt", "closed_at", "closedAt", "updated_at", "updatedAt", "updated_date"),
            GetNestedString(order, "timeslot", "to", "date_to", "end_at"),
            GetNestedString(order, "local_timeslot", "to", "date_to", "end_at"));
    }

    private static bool IsSupplyOrderInRange(OzonSupplyOrderSummary order, DateOnly dateFrom, DateOnly dateTo)
    {
        var value = FirstFilled(order.CreatedAt, order.UpdatedAt);
        if (!TryGetDateOnly(value, out var date))
        {
            return true;
        }

        return date >= dateFrom && date <= dateTo;
    }

    private static bool TryGetDateOnly(string value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (DateTimeOffset.TryParse(
                value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var offset))
        {
            date = DateOnly.FromDateTime(offset.UtcDateTime);
            return true;
        }

        if (DateTime.TryParse(
                value,
                new System.Globalization.CultureInfo("ru-RU"),
                System.Globalization.DateTimeStyles.None,
                out var localDate))
        {
            date = DateOnly.FromDateTime(localDate);
            return true;
        }

        return false;
    }

    private static int GetInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numericResult))
            {
                return numericResult;
            }

            if (value.ValueKind == JsonValueKind.String
                && int.TryParse(value.GetString(), out var textResult))
            {
                return textResult;
            }
        }

        return 0;
    }

    private static decimal GetDecimal(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String
                && decimal.TryParse(value.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var textNumber))
            {
                return textNumber;
            }
        }

        return 0;
    }

    private static decimal GetJsonDecimal(JsonElement value)
    {
        try
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String
                && decimal.TryParse(value.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var textNumber))
            {
                return textNumber;
            }
        }
        catch (FormatException)
        {
            return 0;
        }

        return 0;
    }

    private static decimal GetFirstDecimal(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String
                && decimal.TryParse(value.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var textNumber))
            {
                return textNumber;
            }
        }

        return 0;
    }
}

public record OzonProductListRequest(
    [property: JsonPropertyName("filter")] OzonProductListFilter Filter,
    [property: JsonPropertyName("last_id")] string LastId,
    [property: JsonPropertyName("limit")] int Limit);

public record OzonProductListFilter(
    [property: JsonPropertyName("visibility")] string Visibility);

public record OzonProductListResponse(
    [property: JsonPropertyName("result")] OzonProductListResult Result);

public record OzonProductListResult(
    [property: JsonPropertyName("items")] IReadOnlyList<OzonProductListItem> Items,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("last_id")] string LastId);

public record OzonProductListItem(
    [property: JsonPropertyName("product_id")] long ProductId,
    [property: JsonPropertyName("offer_id")] string OfferId);

public record OzonStockListRequest(
    [property: JsonPropertyName("filter")] OzonProductListFilter Filter,
    [property: JsonPropertyName("last_id")] string LastId,
    [property: JsonPropertyName("limit")] int Limit);

public record OzonStockListResult(
    [property: JsonPropertyName("items")] IReadOnlyList<OzonStockListItem> Items,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("cursor")] string Cursor);

public record OzonStockListItem(
    [property: JsonPropertyName("product_id")] long ProductId,
    [property: JsonPropertyName("offer_id")] string OfferId,
    [property: JsonPropertyName("stocks")] IReadOnlyList<OzonStockItem> Stocks);

public record OzonStockItem(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("present")] int Present,
    [property: JsonPropertyName("reserved")] int Reserved,
    [property: JsonPropertyName("sku")] long Sku);

public record OzonProductInfoListRequest(
    [property: JsonPropertyName("product_id")] IReadOnlyCollection<long> ProductIds);

public record OzonProductInfoListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<OzonProductInfoItem> Items);

public record OzonProductInfoItem(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("offer_id")] string OfferId,
    [property: JsonPropertyName("price")] JsonElement Price,
    [property: JsonPropertyName("old_price")] JsonElement OldPrice,
    [property: JsonPropertyName("min_price")] JsonElement MinPrice,
    [property: JsonPropertyName("currency_code")] string CurrencyCode,
    [property: JsonPropertyName("sku")] long? Sku,
    [property: JsonPropertyName("sources")] IReadOnlyList<OzonProductSource>? Sources,
    [property: JsonPropertyName("images")] IReadOnlyList<string>? Images,
    [property: JsonPropertyName("primary_image")] IReadOnlyList<string>? PrimaryImage,
    [property: JsonPropertyName("statuses")] OzonProductStatuses? Statuses);

public record OzonProductSource(
    [property: JsonPropertyName("sku")] long Sku);

public record OzonProductStatuses(
    [property: JsonPropertyName("status_name")] string StatusName);

public record OzonProductSummary(
    long ProductId,
    string OfferId,
    long? Sku,
    string Name,
    decimal Price,
    decimal OldPrice,
    decimal MinPrice,
    string CurrencyCode,
    string Status,
    string ProductUrl,
    string ImageUrl);

public record OzonStockSummary(
    long ProductId,
    string OfferId,
    long? Sku,
    string Name,
    decimal Price,
    decimal OldPrice,
    decimal MinPrice,
    string CurrencyCode,
    int FboPresent,
    int FbsPresent,
    string ProductUrl,
    string ImageUrl);

public record OzonPriceUpdateRequest(
    long ProductId,
    string OfferId,
    decimal Price,
    decimal? OldPrice,
    decimal? MinPrice,
    string CurrencyCode);

public record OzonImportPricesRequest(
    [property: JsonPropertyName("prices")] IReadOnlyList<OzonImportPriceItem> Prices);

public record OzonImportPriceItem(
    [property: JsonPropertyName("product_id")] long ProductId,
    [property: JsonPropertyName("offer_id")] string OfferId,
    [property: JsonPropertyName("price")] string Price,
    [property: JsonPropertyName("old_price")] string OldPrice,
    [property: JsonPropertyName("min_price")] string MinPrice,
    [property: JsonPropertyName("currency_code")] string CurrencyCode);

public record OzonPriceUpdateResult(bool Success, string Message, JsonElement Raw);

public record OzonAnalyticsResult(
    IReadOnlyList<OzonAnalyticsRow> Rows,
    IReadOnlyList<OzonTopProductRow> TopProducts,
    IReadOnlyList<OzonSalesDay> DailySales,
    decimal OrderedUnitsTotal,
    decimal RevenueTotal,
    decimal CommissionTotal,
    decimal PayoutTotal,
    decimal LogisticsTotal,
    decimal ServicesTotal,
    int AwaitingPackagingCount,
    int AwaitingDeliverCount,
    int DeliveringCount,
    int DeliveredCount,
    OzonAccountBalance AccountBalance,
    string Timestamp);

public record OzonAccountBalance(decimal Amount, string CurrencyCode);

public record OzonSalesDay(string Date, decimal Quantity, decimal Revenue);

public record OzonAnalyticsRow(
    long Sku,
    string OfferId,
    string ProductName,
    string Status,
    string PostingNumber,
    decimal Quantity,
    decimal Revenue,
    decimal CommissionPercent,
    decimal CommissionAmount,
    decimal Payout,
    string CurrencyCode,
    decimal LogisticsAmount);

public record OzonTopProductRow(
    long Sku,
    string OfferId,
    string ProductName,
    decimal Quantity,
    decimal Revenue,
    string CurrencyCode);

public record OzonPostingListRequest(
    [property: JsonPropertyName("dir")] string Dir,
    [property: JsonPropertyName("filter")] OzonPostingFilter Filter,
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("with")] OzonPostingWith With);

public record OzonPostingFilter(
    [property: JsonPropertyName("since")] string Since,
    [property: JsonPropertyName("to")] string To,
    [property: JsonPropertyName("status")] string Status);

public record OzonPostingWith(
    [property: JsonPropertyName("analytics_data")] bool AnalyticsData,
    [property: JsonPropertyName("financial_data")] bool FinancialData);

public record OzonPostingListResponse(
    [property: JsonPropertyName("result")] OzonPostingListResult Result);

public record OzonFboPostingListResponse(
    [property: JsonPropertyName("result")] IReadOnlyList<OzonPosting> Result);

public record OzonPostingListResult(
    [property: JsonPropertyName("postings")] IReadOnlyList<OzonPosting> Postings,
    [property: JsonPropertyName("has_next")] bool HasNext);

public record OzonPosting(
    [property: JsonPropertyName("posting_number")] string PostingNumber,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("in_process_at")] string InProcessAt,
    [property: JsonPropertyName("products")] IReadOnlyList<OzonPostingProduct> Products,
    [property: JsonPropertyName("analytics_data")] OzonPostingAnalyticsData? AnalyticsData,
    [property: JsonPropertyName("financial_data")] OzonPostingFinancialData? FinancialData);

public record OzonPostingAnalyticsData(
    [property: JsonPropertyName("warehouse_name")] string WarehouseName);

public record OzonPostingProduct(
    [property: JsonPropertyName("sku")] long Sku,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("quantity")] decimal Quantity,
    [property: JsonPropertyName("offer_id")] string OfferId,
    [property: JsonPropertyName("product_id")] long ProductId,
    [property: JsonPropertyName("price")]
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    decimal Price,
    [property: JsonPropertyName("currency_code")] string CurrencyCode);

public record OzonPostingFinancialData(
    [property: JsonPropertyName("products")] IReadOnlyList<OzonPostingFinancialProduct> Products);

public record OzonPostingFinancialProduct(
    [property: JsonPropertyName("product_id")] long ProductId,
    [property: JsonPropertyName("commission_amount")] decimal CommissionAmount,
    [property: JsonPropertyName("commission_percent")] decimal CommissionPercent,
    [property: JsonPropertyName("payout")] decimal Payout);

public record OzonFinanceTransactionRequest(
    [property: JsonPropertyName("filter")] OzonFinanceFilter Filter,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize);

public record OzonFinanceTransactionTotalsRequest(
    [property: JsonPropertyName("date")] OzonFinanceDateRange Date,
    [property: JsonPropertyName("posting_number")] string PostingNumber,
    [property: JsonPropertyName("transaction_type")] string TransactionType);

public record OzonFinanceFilter(
    [property: JsonPropertyName("date")] OzonFinanceDateRange Date,
    [property: JsonPropertyName("operation_type")] IReadOnlyList<string> OperationType,
    [property: JsonPropertyName("posting_number")] string PostingNumber,
    [property: JsonPropertyName("transaction_type")] string TransactionType);

public record OzonFinanceDateRange(
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("to")] string To);

public record OzonCashFlowStatementRequest(
    [property: JsonPropertyName("date")] OzonFinanceDateRange Date,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize);

public record OzonSupplyOrderListV2Request(
    [property: JsonPropertyName("from_supply_order_id")] long FromSupplyOrderId,
    [property: JsonPropertyName("limit")] int Limit);

public record OzonSupplyOrderGetRequest(
    [property: JsonPropertyName("supply_order_id")] long SupplyOrderId);

public record OzonSupplyOrderListRequest(
    [property: JsonPropertyName("since")] string Since,
    [property: JsonPropertyName("to")] string To,
    [property: JsonPropertyName("filter")] OzonSupplyOrderFilter Filter,
    [property: JsonPropertyName("limit")] int Limit);

public record OzonSupplyOrderFilter(
    [property: JsonPropertyName("status")] IReadOnlyList<string> Status);

public record OzonSupplyOrderSummary(
    string Id,
    string Status,
    string WarehouseName,
    string CreatedAt,
    string UpdatedAt,
    int ItemsCount);

public record OzonFinanceTransactionResponse(
    [property: JsonPropertyName("result")] OzonFinanceTransactionResult Result);

public record OzonFinanceTransactionResult(
    [property: JsonPropertyName("operations")] IReadOnlyList<OzonFinanceOperation> Operations,
    [property: JsonPropertyName("page_count")] int PageCount,
    [property: JsonPropertyName("row_count")] int RowCount);

public record OzonFinanceOperation(
    [property: JsonPropertyName("operation_id")] long OperationId,
    [property: JsonPropertyName("operation_type_name")] string OperationTypeName,
    [property: JsonPropertyName("accruals_for_sale")] decimal AccrualsForSale,
    [property: JsonPropertyName("sale_commission")] decimal SaleCommission,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("posting")] OzonFinancePosting Posting,
    [property: JsonPropertyName("items")] IReadOnlyList<OzonFinanceItem> Items,
    [property: JsonPropertyName("services")] IReadOnlyList<OzonFinanceService> Services);

public record OzonFinancePosting(
    [property: JsonPropertyName("posting_number")] string PostingNumber);

public record OzonFinanceItem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("sku")] long Sku);

public record OzonFinanceService(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("price")] decimal Price);

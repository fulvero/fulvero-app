using Fulvero.Api.Data;
using Fulvero.Api.Models;
using Fulvero.Api.Ozon;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fulvero.Api.Telegram;

public class TelegramNotificationService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<TelegramOptions> options,
    ILogger<TelegramNotificationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendNotificationsAsync(stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Telegram notifications failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(Math.Max(15, options.CurrentValue.NotificationIntervalMinutes)), stoppingToken);
        }
    }

    private async Task SendNotificationsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bot = scope.ServiceProvider.GetRequiredService<TelegramBotClient>();
        if (!bot.IsConfigured)
        {
            return;
        }

        var integrations = await db.TelegramIntegrations
            .Include(item => item.Company)
            .Where(item => item.Enabled && item.ChatId != null)
            .ToListAsync(cancellationToken);

        foreach (var integration in integrations)
        {
            await SendCompanyNotificationsAsync(scope, integration, cancellationToken);
        }
    }

    private static async Task SendCompanyNotificationsAsync(
        IServiceScope scope,
        TelegramIntegration integration,
        CancellationToken cancellationToken)
    {
        if (integration.ChatId is null)
        {
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bot = scope.ServiceProvider.GetRequiredService<TelegramBotClient>();
        var now = DateTimeOffset.UtcNow;
        var todayKey = now.ToString("yyyyMMdd");

        await SendSubscriptionNotificationsAsync(db, bot, integration, now, todayKey, cancellationToken);
        await SendSupplyNotificationsAsync(db, bot, integration, now, todayKey, cancellationToken);
        await SendStockAndProductionNotificationsAsync(scope, integration, now, todayKey, cancellationToken);
    }

    private static async Task SendSubscriptionNotificationsAsync(
        AppDbContext db,
        TelegramBotClient bot,
        TelegramIntegration integration,
        DateTimeOffset now,
        string todayKey,
        CancellationToken cancellationToken)
    {
        var company = integration.Company;
        if (company.SubscriptionStatus == CompanySubscriptionStatuses.Active && company.SubscriptionPaidUntil is not null)
        {
            var daysLeft = (company.SubscriptionPaidUntil.Value - now).TotalDays;
            if (daysLeft <= 3 && daysLeft > 0)
            {
                await SendOnceAsync(
                    db,
                    bot,
                    integration,
                    $"telegram:subscription-soon:{company.Id}:{todayKey}",
                    "⚠️ Подписка Fulvero заканчивается через 3 дня.",
                    cancellationToken);
            }
        }

        if (!SubscriptionAccess.IsActive(company))
        {
            await SendOnceAsync(
                db,
                bot,
                integration,
                $"telegram:subscription-expired:{company.Id}:{todayKey}",
                "🚨 Подписка истекла.\n\nДля продолжения работы оплатите тариф.",
                cancellationToken);
        }
    }

    private static async Task SendSupplyNotificationsAsync(
        AppDbContext db,
        TelegramBotClient bot,
        TelegramIntegration integration,
        DateTimeOffset now,
        string todayKey,
        CancellationToken cancellationToken)
    {
        var overdueSupplies = await db.Supplies
            .AsNoTracking()
            .Include(supply => supply.Items)
            .Where(supply => supply.CompanyId == integration.CompanyId
                && !supply.IsArchived
                && supply.Status != SupplyStatuses.Accepted
                && supply.CreatedAt < now.AddDays(-7))
            .ToListAsync(cancellationToken);

        foreach (var supply in overdueSupplies)
        {
            var supplier = supply.Items.Select(item => item.SupplierName).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "не указан";
            var days = Math.Max(1, (int)Math.Floor((now - supply.CreatedAt).TotalDays - 7));
            await SendOnceAsync(
                db,
                bot,
                integration,
                $"telegram:supply-overdue:{supply.Id}:{todayKey}",
                $"⚠️ Просрочена поставка\n\nПоставщик: {TelegramText.Escape(supplier)}\nОжидалась: {TelegramText.FormatDate(supply.CreatedAt.AddDays(7))}\n\nПросрочка: {days} дн.",
                cancellationToken);
        }
    }

    private static async Task SendStockAndProductionNotificationsAsync(
        IServiceScope scope,
        TelegramIntegration integration,
        DateTimeOffset now,
        string todayKey,
        CancellationToken cancellationToken)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dataProtectionProvider = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        var ozonOptions = scope.ServiceProvider.GetRequiredService<IOptions<OzonOptions>>().Value;
        var ozonApi = scope.ServiceProvider.GetRequiredService<OzonApiClient>();
        var bot = scope.ServiceProvider.GetRequiredService<TelegramBotClient>();
        var protector = dataProtectionProvider.CreateProtector("Fulvero.OzonCredentials.v1");
        var credentials = await CompanyAccess.GetOzonCredentialsForCompanyAsync(db, integration.CompanyId, protector, ozonOptions, cancellationToken);
        if (string.IsNullOrWhiteSpace(credentials.ClientId) || string.IsNullOrWhiteSpace(credentials.ApiKey))
        {
            return;
        }

        ozonApi.UseCredentials(credentials.ClientId, credentials.ApiKey);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var stocks = await ozonApi.GetStockSummariesAsync(100, cancellationToken);
        var analytics = await ozonApi.GetAnalyticsAsync(today.AddDays(-29), today, cancellationToken);
        var topBySku = analytics.TopProducts
            .Where(item => item.Sku > 0)
            .ToDictionary(item => item.Sku, item => item.Quantity);
        var settings = await db.ProductSettings
            .AsNoTracking()
            .Where(item => item.CompanyId == integration.CompanyId)
            .ToDictionaryAsync(item => item.OzonProductId, cancellationToken);

        foreach (var stock in stocks)
        {
            var total = stock.FboPresent + stock.FbsPresent;
            var sold = stock.Sku is not null && topBySku.TryGetValue(stock.Sku.Value, out var quantity)
                ? quantity
                : 0;
            var average = sold / 30m;
            if (average <= 0)
            {
                continue;
            }

            var daysLeft = total <= 0 ? 0 : total / average;
            if (total <= 0)
            {
                await SendOnceAsync(
                    db,
                    bot,
                    integration,
                    $"telegram:stock-empty:{stock.ProductId}:{todayKey}",
                    $"🚨 Товар закончился\n\n{TelegramText.Escape(stock.Name)}\n\nОстаток: 0 шт.",
                    cancellationToken);
            }
            else if (daysLeft <= 5)
            {
                await SendOnceAsync(
                    db,
                    bot,
                    integration,
                    $"telegram:stock-low:{stock.ProductId}:{todayKey}",
                    $"⚠️ Заканчивается товар\n\n{TelegramText.Escape(stock.Name)}\n\nОстаток: {total} шт.\nСредние продажи: {average:0.#} шт./день\n\nХватит примерно на {Math.Ceiling(daysLeft)} дн.",
                    cancellationToken);
            }

            if (settings.TryGetValue(stock.ProductId, out var setting)
                && setting.ProductType == ProductTypes.Production
                && daysLeft <= 5)
            {
                var recommended = Math.Max(1, (int)Math.Ceiling(average * 30));
                await SendOnceAsync(
                    db,
                    bot,
                    integration,
                    $"telegram:production-needed:{stock.ProductId}:{todayKey}",
                    $"🏭 Требуется производство\n\n{TelegramText.Escape(stock.Name)}\n\nРекомендуемый объем:\n{recommended} шт.",
                    cancellationToken);
            }
        }
    }

    public static async Task SendOnceAsync(
        AppDbContext db,
        TelegramBotClient bot,
        TelegramIntegration integration,
        string key,
        string message,
        CancellationToken cancellationToken)
    {
        if (integration.ChatId is null)
        {
            return;
        }

        var exists = await db.TelegramNotificationStates.AnyAsync(
            item => item.CompanyId == integration.CompanyId && item.NotificationKey == key,
            cancellationToken);
        if (exists)
        {
            return;
        }

        await bot.SendMessageAsync(integration.ChatId.Value, message, cancellationToken);
        db.TelegramNotificationStates.Add(new TelegramNotificationState
        {
            CompanyId = integration.CompanyId,
            NotificationKey = key,
            SentAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}

using Fulvero.Api.Data;
using Fulvero.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fulvero.Api.Telegram;

public class TelegramUpdatePollingService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<TelegramOptions> options,
    ILogger<TelegramUpdatePollingService> logger) : BackgroundService
{
    private const string StateId = "default";
    private bool webhookChecked;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Telegram polling started. Enabled={Enabled}, HasToken={HasToken}, BotUsername={BotUsername}, IntervalSeconds={IntervalSeconds}.",
            options.CurrentValue.Enabled,
            !string.IsNullOrWhiteSpace(options.CurrentValue.BotToken),
            options.CurrentValue.BotUsername,
            Math.Max(2, options.CurrentValue.PollingIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Telegram update polling failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(2, options.CurrentValue.PollingIntervalSeconds)), stoppingToken);
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var bot = scope.ServiceProvider.GetRequiredService<TelegramBotClient>();
        if (!bot.IsConfigured)
        {
            logger.LogDebug("Telegram polling skipped: bot is not configured.");
            return;
        }

        await EnsureWebhookDisabledAsync(bot, cancellationToken);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var state = await GetStateAsync(db, cancellationToken);
        var updates = await bot.GetUpdatesAsync(state.LastProcessedUpdateId, cancellationToken);
        if (updates is null || !updates.Ok)
        {
            logger.LogWarning("Telegram getUpdates returned empty or failed response.");
            return;
        }

        logger.LogInformation(
            "Telegram getUpdates received {Count} updates with offset {Offset}.",
            updates.Result.Count,
            state.LastProcessedUpdateId);

        foreach (var update in updates.Result)
        {
            if (update.UpdateId < state.LastProcessedUpdateId)
            {
                logger.LogDebug(
                    "Telegram update {UpdateId} skipped because current offset is {Offset}.",
                    update.UpdateId,
                    state.LastProcessedUpdateId);
                continue;
            }

            try
            {
                await ProcessUpdateAsync(scope, update, cancellationToken);

                state.LastProcessedUpdateId = update.UpdateId + 1;
                state.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation(
                    "Telegram update {UpdateId} processed. Next offset is {Offset}.",
                    update.UpdateId,
                    state.LastProcessedUpdateId);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to process Telegram update {UpdateId}.", update.UpdateId);
            }
        }
    }

    private async Task EnsureWebhookDisabledAsync(TelegramBotClient bot, CancellationToken cancellationToken)
    {
        if (webhookChecked)
        {
            return;
        }

        webhookChecked = true;
        var webhookInfo = await bot.GetWebhookInfoAsync(cancellationToken);
        if (webhookInfo?.Ok != true)
        {
            logger.LogWarning("Telegram webhook info is unavailable.");
            return;
        }

        var webhookUrl = webhookInfo.Result.Url;
        logger.LogInformation(
            "Telegram webhook info: HasWebhook={HasWebhook}, WebhookUrl={WebhookUrl}, PendingUpdates={PendingUpdates}, LastError={LastError}.",
            !string.IsNullOrWhiteSpace(webhookUrl),
            SanitizeWebhookUrl(webhookUrl),
            webhookInfo.Result.PendingUpdateCount,
            webhookInfo.Result.LastErrorMessage ?? "-");

        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            return;
        }

        var deleted = await bot.DeleteWebhookAsync(cancellationToken);
        logger.LogInformation("Telegram webhook removed before polling: {Deleted}.", deleted);
    }

    private async Task ProcessUpdateAsync(
        IServiceScope scope,
        TelegramUpdate update,
        CancellationToken cancellationToken)
    {
        var message = update.Message;
        var type = message is not null
            ? "message"
            : update.EditedMessage is not null
                ? "edited_message"
                : update.ChannelPost is not null
                    ? "channel_post"
                    : "unknown";
        logger.LogInformation("Telegram update {UpdateId}: Type={Type}.", update.UpdateId, type);

        if (message is null)
        {
            return;
        }

        logger.LogInformation(
            "Telegram message update {UpdateId}: ChatId={ChatId}, ChatType={ChatType}, UserId={UserId}, Username={Username}, Text={Text}.",
            update.UpdateId,
            message.Chat.Id,
            message.Chat.Type,
            message.From?.Id,
            message.From?.Username ?? "-",
            message.Text ?? "<empty>");

        if (string.IsNullOrWhiteSpace(message.Text))
        {
            return;
        }

        var startPayload = TryParseStartPayload(message.Text);
        if (!startPayload.IsStart)
        {
            logger.LogDebug("Telegram message {UpdateId} is not /start command.", update.UpdateId);
            return;
        }

        if (string.IsNullOrWhiteSpace(startPayload.LinkCode))
        {
            logger.LogInformation("Telegram /start without link code from chat {ChatId}.", message.Chat.Id);
            var bot = scope.ServiceProvider.GetRequiredService<TelegramBotClient>();
            await bot.SendMessageAsync(
                message.Chat.Id,
                "Откройте бота через кнопку в Fulvero, чтобы привязать компанию.",
                cancellationToken);
            return;
        }

        logger.LogInformation(
            "Telegram /start link code parsed. ChatId={ChatId}, LinkCode={LinkCode}.",
            message.Chat.Id,
            startPayload.LinkCode);
        await LinkChatAsync(scope, startPayload.LinkCode, message, cancellationToken);
    }

    private static StartPayload TryParseStartPayload(string text)
    {
        var trimmed = text.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new StartPayload(false, null);
        }

        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0];
        if (command.StartsWith("/start_", StringComparison.OrdinalIgnoreCase))
        {
            return new StartPayload(true, command["/start_".Length..].Trim());
        }

        if (command.StartsWith("/start@", StringComparison.OrdinalIgnoreCase))
        {
            var underscoreIndex = command.IndexOf('_', StringComparison.Ordinal);
            if (underscoreIndex >= 0 && underscoreIndex + 1 < command.Length)
            {
                return new StartPayload(true, command[(underscoreIndex + 1)..].Trim());
            }

            return new StartPayload(true, parts.Length >= 2 ? parts[1].Trim() : null);
        }

        if (!command.Equals("/start", StringComparison.OrdinalIgnoreCase))
        {
            return new StartPayload(false, null);
        }

        if (parts.Length >= 2)
        {
            return new StartPayload(true, parts[1].Trim());
        }

        var underscorePayload = trimmed.Length > "/start_".Length && trimmed.StartsWith("/start_", StringComparison.OrdinalIgnoreCase)
            ? trimmed["/start_".Length..].Trim()
            : null;
        return new StartPayload(true, underscorePayload);
    }

    private readonly record struct StartPayload(bool IsStart, string? LinkCode);

    private static async Task<TelegramBotState> GetStateAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var state = await db.TelegramBotStates.FirstOrDefaultAsync(item => item.Id == StateId, cancellationToken);
        if (state is not null)
        {
            return state;
        }

        state = new TelegramBotState { Id = StateId };
        db.TelegramBotStates.Add(state);
        await db.SaveChangesAsync(cancellationToken);
        return state;
    }

    private static async Task LinkChatAsync(
        IServiceScope scope,
        string code,
        TelegramMessage message,
        CancellationToken cancellationToken)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bot = scope.ServiceProvider.GetRequiredService<TelegramBotClient>();
        var chat = message.Chat;
        var integration = await db.TelegramIntegrations
            .Include(item => item.Company)
            .FirstOrDefaultAsync(item => item.LinkCode == code, cancellationToken);

        if (integration is null)
        {
            await bot.SendMessageAsync(chat.Id, "Код привязки не найден или устарел. Создайте новый код в настройках Fulvero.", cancellationToken);
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<TelegramUpdatePollingService>>();
            logger.LogWarning("Telegram link code was not found. ChatId={ChatId}, LinkCode={LinkCode}.", chat.Id, code);
            return;
        }

        var linkLogger = scope.ServiceProvider.GetRequiredService<ILogger<TelegramUpdatePollingService>>();
        linkLogger.LogInformation(
            "Telegram integration found. CompanyId={CompanyId}, ChatId={ChatId}, ExistingChatId={ExistingChatId}, LinkedAt={LinkedAt}.",
            integration.CompanyId,
            chat.Id,
            integration.ChatId,
            integration.LinkedAt);

        if (integration.ChatId == chat.Id && integration.LinkedAt is not null)
        {
            linkLogger.LogInformation(
                "Telegram chat is already linked. CompanyId={CompanyId}, ChatId={ChatId}, UserId={UserId}, Username={Username}.",
                integration.CompanyId,
                chat.Id,
                message.From?.Id,
                message.From?.Username ?? "-");
            await bot.SendMessageAsync(
                chat.Id,
                "Telegram успешно подключён к Fulvero ✅",
                cancellationToken);
            return;
        }

        integration.ChatId = chat.Id;
        integration.ChatTitle = GetChatTitle(chat, message.From);
        integration.LinkedAt = DateTimeOffset.UtcNow;
        integration.Enabled = true;
        await db.SaveChangesAsync(cancellationToken);

        linkLogger.LogInformation(
            "Telegram chat linked. CompanyId={CompanyId}, ChatId={ChatId}, UserId={UserId}, Username={Username}.",
            integration.CompanyId,
            chat.Id,
            message.From?.Id,
            message.From?.Username ?? "-");

        await bot.SendMessageAsync(
            chat.Id,
            "Telegram успешно подключён к Fulvero ✅",
            cancellationToken);
    }

    private static string GetChatTitle(TelegramChat chat, TelegramUser? user)
    {
        var username = user?.Username ?? chat.Username;
        if (!string.IsNullOrWhiteSpace(username))
        {
            return $"@{username.Trim().TrimStart('@')}";
        }

        if (!string.IsNullOrWhiteSpace(chat.Title))
        {
            return chat.Title.Trim();
        }

        return string.Join(" ", new[] { user?.FirstName, user?.LastName, chat.FirstName, chat.LastName }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
    }

    private static string SanitizeWebhookUrl(string webhookUrl)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            return "-";
        }

        if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri))
        {
            return "<invalid-url>";
        }

        return uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.SafeUnescaped);
    }
}

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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var state = await GetStateAsync(db, cancellationToken);
        var updates = await bot.GetUpdatesAsync(state.LastProcessedUpdateId + 1, cancellationToken);
        if (updates is null || !updates.Ok)
        {
            return;
        }

        foreach (var update in updates.Result)
        {
            if (update.UpdateId <= state.LastProcessedUpdateId)
            {
                continue;
            }

            try
            {
                var message = update.Message;
                if (message is not null && !string.IsNullOrWhiteSpace(message.Text))
                {
                    var parts = message.Text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && parts[0].Equals("/start", StringComparison.OrdinalIgnoreCase))
                    {
                        await LinkChatAsync(scope, parts[1], message.Chat, cancellationToken);
                    }
                }

                state.LastProcessedUpdateId = update.UpdateId;
                state.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to process Telegram update {UpdateId}.", update.UpdateId);
            }
        }
    }

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
        TelegramChat chat,
        CancellationToken cancellationToken)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bot = scope.ServiceProvider.GetRequiredService<TelegramBotClient>();
        var integration = await db.TelegramIntegrations
            .Include(item => item.Company)
            .FirstOrDefaultAsync(item => item.LinkCode == code, cancellationToken);
        if (integration is null)
        {
            await bot.SendMessageAsync(chat.Id, "Код привязки Fulvero не найден или устарел.", cancellationToken);
            return;
        }

        if (integration.ChatId == chat.Id && integration.LinkedAt is not null)
        {
            return;
        }

        integration.ChatId = chat.Id;
        integration.ChatTitle = GetChatTitle(chat);
        integration.LinkedAt = DateTimeOffset.UtcNow;
        integration.Enabled = true;
        await db.SaveChangesAsync(cancellationToken);

        await bot.SendMessageAsync(
            chat.Id,
            $"✅ Fulvero подключен к Telegram\n\nКомпания: {TelegramText.Escape(integration.Company.Name)}\n\nУведомления будут приходить сюда.",
            cancellationToken);
    }

    private static string GetChatTitle(TelegramChat chat)
    {
        var title = chat.Title;
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title.Trim();
        }

        return string.Join(" ", new[] { chat.FirstName, chat.LastName, chat.Username }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
    }
}

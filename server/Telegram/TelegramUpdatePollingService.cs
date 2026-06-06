using Fulvero.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fulvero.Api.Telegram;

public class TelegramUpdatePollingService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<TelegramOptions> options,
    ILogger<TelegramUpdatePollingService> logger) : BackgroundService
{
    private long offset;

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

        var updates = await bot.GetUpdatesAsync(offset, cancellationToken);
        if (updates is null || !updates.Ok)
        {
            return;
        }

        foreach (var update in updates.Result)
        {
            offset = Math.Max(offset, update.UpdateId + 1);
            var message = update.Message;
            if (message is null || string.IsNullOrWhiteSpace(message.Text))
            {
                continue;
            }

            var parts = message.Text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !parts[0].Equals("/start", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            await LinkChatAsync(scope, parts[1], message.Chat, cancellationToken);
        }
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

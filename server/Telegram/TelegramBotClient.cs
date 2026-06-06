using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Fulvero.Api.Telegram;

public class TelegramBotClient(HttpClient httpClient, IOptionsMonitor<TelegramOptions> options, ILogger<TelegramBotClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public bool IsConfigured => options.CurrentValue.Enabled && !string.IsNullOrWhiteSpace(options.CurrentValue.BotToken);

    public string GetStartUrl(string code)
    {
        var username = options.CurrentValue.BotUsername.Trim().TrimStart('@');
        return string.IsNullOrWhiteSpace(username)
            ? string.Empty
            : $"https://t.me/{username}?start={Uri.EscapeDataString(code)}";
    }

    public async Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            logger.LogInformation("Telegram bot is disabled. Skipped message to {ChatId}.", chatId);
            return;
        }

        var token = options.CurrentValue.BotToken;
        using var response = await httpClient.PostAsJsonAsync(
            $"https://api.telegram.org/bot{token}/sendMessage",
            new
            {
                chat_id = chatId,
                text,
                parse_mode = "HTML",
                disable_web_page_preview = true
            },
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Telegram sendMessage failed: {Status} {Body}", response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }
    }

    public async Task<TelegramUpdatesResponse?> GetUpdatesAsync(long offset, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var token = options.CurrentValue.BotToken;
        var url = $"https://api.telegram.org/bot{token}/getUpdates?timeout=25&offset={offset}";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Telegram getUpdates failed: {Status} {Body}", response.StatusCode, body);
            return null;
        }

        return JsonSerializer.Deserialize<TelegramUpdatesResponse>(body, JsonOptions);
    }
}

public record TelegramUpdatesResponse(bool Ok, IReadOnlyList<TelegramUpdate> Result);
public record TelegramUpdate(long UpdateId, TelegramMessage? Message);
public record TelegramMessage(long MessageId, TelegramChat Chat, string Text);
public record TelegramChat(long Id, string Type, string? Title, string? Username, string? FirstName, string? LastName);

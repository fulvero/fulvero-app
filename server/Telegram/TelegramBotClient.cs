using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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

        var result = JsonSerializer.Deserialize<TelegramUpdatesResponse>(body, JsonOptions);
        if (result?.Ok == false)
        {
            logger.LogWarning("Telegram getUpdates API returned ok=false: {Body}", body);
        }

        return result;
    }

    public async Task<TelegramWebhookInfoResponse?> GetWebhookInfoAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var token = options.CurrentValue.BotToken;
        using var response = await httpClient.GetAsync($"https://api.telegram.org/bot{token}/getWebhookInfo", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Telegram getWebhookInfo failed for bot token {Token}: {Status} {Body}",
                MaskToken(token),
                response.StatusCode,
                body);
            return null;
        }

        return JsonSerializer.Deserialize<TelegramWebhookInfoResponse>(body, JsonOptions);
    }

    public async Task<bool> DeleteWebhookAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return false;
        }

        var token = options.CurrentValue.BotToken;
        using var response = await httpClient.GetAsync(
            $"https://api.telegram.org/bot{token}/deleteWebhook?drop_pending_updates=false",
            cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Telegram deleteWebhook failed for bot token {Token}: {Status} {Body}",
                MaskToken(token),
                response.StatusCode,
                body);
            return false;
        }

        var result = JsonSerializer.Deserialize<TelegramBoolResponse>(body, JsonOptions);
        return result?.Ok == true && result.Result;
    }

    private static string MaskToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return "<empty>";
        }

        return token.Length <= 8 ? "***" : $"{token[..4]}...{token[^4..]}";
    }
}

public record TelegramUpdatesResponse(bool Ok, IReadOnlyList<TelegramUpdate> Result);
public record TelegramWebhookInfoResponse(bool Ok, TelegramWebhookInfo Result);
public record TelegramWebhookInfo(
    string Url,
    [property: JsonPropertyName("pending_update_count")] int PendingUpdateCount,
    [property: JsonPropertyName("last_error_message")] string? LastErrorMessage);
public record TelegramBoolResponse(bool Ok, bool Result, string? Description);
public record TelegramUpdate(
    [property: JsonPropertyName("update_id")] long UpdateId,
    TelegramMessage? Message,
    [property: JsonPropertyName("edited_message")] TelegramMessage? EditedMessage,
    [property: JsonPropertyName("channel_post")] TelegramMessage? ChannelPost);
public record TelegramMessage(
    [property: JsonPropertyName("message_id")] long MessageId,
    TelegramChat Chat,
    TelegramUser? From,
    string? Text);
public record TelegramChat(
    long Id,
    string Type,
    string? Title,
    string? Username,
    [property: JsonPropertyName("first_name")] string? FirstName,
    [property: JsonPropertyName("last_name")] string? LastName);
public record TelegramUser(
    long Id,
    [property: JsonPropertyName("is_bot")] bool IsBot,
    [property: JsonPropertyName("first_name")] string? FirstName,
    [property: JsonPropertyName("last_name")] string? LastName,
    string? Username);

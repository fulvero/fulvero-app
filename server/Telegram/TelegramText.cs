namespace Fulvero.Api.Telegram;

public static class TelegramText
{
    public static string Escape(string value) =>
        System.Net.WebUtility.HtmlEncode(value);

    public static string FormatDate(DateTimeOffset value) =>
        value.ToLocalTime().ToString("dd.MM.yyyy");
}

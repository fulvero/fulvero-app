namespace Fulvero.Api.Mail;

public class SmtpOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 465;
    public bool EnableSsl { get; set; } = true;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromEmail { get; set; } = string.Empty;
    public string FromName { get; set; } = "Fulvero";
    public string ReplyToEmail { get; set; } = string.Empty;
    public string LogoFilePath { get; set; } = "wwwroot/email-logo.png";
    public string LogoUrl { get; set; } = "https://fulvero.ru/assets/fulvero-logo.png";
}

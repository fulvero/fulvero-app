using System.Net;
using System.Net.Security;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;

namespace Fulvero.Api.Mail;

public class EmailSender(
    IOptionsMonitor<SmtpOptions> options,
    IWebHostEnvironment environment,
    ILogger<EmailSender> logger)
{
    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        var value = options.CurrentValue;
        if (!value.Enabled)
        {
            logger.LogInformation("Email sending is disabled. Skipped message to {Email}: {Subject}", toEmail, subject);
            return;
        }

        if (string.IsNullOrWhiteSpace(toEmail)
            || string.IsNullOrWhiteSpace(value.Host)
            || string.IsNullOrWhiteSpace(value.UserName)
            || string.IsNullOrWhiteSpace(value.Password)
            || string.IsNullOrWhiteSpace(value.FromEmail))
        {
            logger.LogWarning("SMTP is not fully configured. Skipped message to {Email}: {Subject}", toEmail, subject);
            return;
        }

        try
        {
            var logo = LoadLogoBytes(value);
            await SendSmtpAsync(value, toEmail, subject, WrapHtml(htmlBody, logo is not null), logo, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to send email to {Email}: {Subject}", toEmail, subject);
        }
    }

    private static async Task SendSmtpAsync(
        SmtpOptions options,
        string toEmail,
        string subject,
        string htmlBody,
        byte[]? logoBytes,
        CancellationToken cancellationToken)
    {
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(options.Host, options.Port, cancellationToken);

        Stream stream = tcpClient.GetStream();
        SslStream? sslStream = null;
        if (options.EnableSsl && options.Port == 465)
        {
            sslStream = new SslStream(stream);
            await sslStream.AuthenticateAsClientAsync(options.Host);
            stream = sslStream;
        }

        using var reader = new StreamReader(stream, Encoding.ASCII);
        using var writer = new StreamWriter(stream, Encoding.ASCII)
        {
            NewLine = "\r\n",
            AutoFlush = true
        };

        await ExpectAsync(reader, 220, cancellationToken);
        await CommandAsync(reader, writer, $"EHLO {GetDomain(options.FromEmail)}", 250, cancellationToken);

        if (options.EnableSsl && options.Port != 465)
        {
            await CommandAsync(reader, writer, "STARTTLS", 220, cancellationToken);
            sslStream = new SslStream(stream);
            await sslStream.AuthenticateAsClientAsync(options.Host);
            stream = sslStream;
        }

        if (sslStream is not null && options.Port != 465)
        {
            using var secureReader = new StreamReader(stream, Encoding.ASCII);
            using var secureWriter = new StreamWriter(stream, Encoding.ASCII)
            {
                NewLine = "\r\n",
                AutoFlush = true
            };
            await CommandAsync(secureReader, secureWriter, $"EHLO {GetDomain(options.FromEmail)}", 250, cancellationToken);
            await AuthenticateAsync(secureReader, secureWriter, options, cancellationToken);
            await SendMessageAsync(secureReader, secureWriter, options, toEmail, subject, htmlBody, logoBytes, cancellationToken);
            return;
        }

        await AuthenticateAsync(reader, writer, options, cancellationToken);
        await SendMessageAsync(reader, writer, options, toEmail, subject, htmlBody, logoBytes, cancellationToken);
    }

    private static async Task AuthenticateAsync(
        StreamReader reader,
        StreamWriter writer,
        SmtpOptions options,
        CancellationToken cancellationToken)
    {
        await CommandAsync(reader, writer, "AUTH LOGIN", 334, cancellationToken);
        await CommandAsync(reader, writer, Convert.ToBase64String(Encoding.UTF8.GetBytes(options.UserName)), 334, cancellationToken);
        await CommandAsync(reader, writer, Convert.ToBase64String(Encoding.UTF8.GetBytes(options.Password)), 235, cancellationToken);
    }

    private static async Task SendMessageAsync(
        StreamReader reader,
        StreamWriter writer,
        SmtpOptions options,
        string toEmail,
        string subject,
        string htmlBody,
        byte[]? logoBytes,
        CancellationToken cancellationToken)
    {
        var from = new MailAddress(options.FromEmail, options.FromName, Encoding.UTF8);
        var to = new MailAddress(toEmail);
        await CommandAsync(reader, writer, $"MAIL FROM:<{from.Address}>", 250, cancellationToken);
        await CommandAsync(reader, writer, $"RCPT TO:<{to.Address}>", 250, cancellationToken);
        await CommandAsync(reader, writer, "DATA", 354, cancellationToken);
        await writer.WriteLineAsync(ComposeMessage(options, from, to, subject, htmlBody, logoBytes).AsMemory(), cancellationToken);
        await writer.WriteLineAsync(".");
        await ExpectAsync(reader, 250, cancellationToken);
        await CommandAsync(reader, writer, "QUIT", 221, cancellationToken);
    }

    private static string ComposeMessage(
        SmtpOptions options,
        MailAddress from,
        MailAddress to,
        string subject,
        string htmlBody,
        byte[]? logoBytes)
    {
        var bodyBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(htmlBody));
        var boundary = $"fulvero-{Guid.NewGuid():N}";
        var builder = new StringBuilder();
        builder.AppendLine($"From: {EncodeAddress(from)}");
        builder.AppendLine($"To: {EncodeAddress(to)}");
        builder.AppendLine($"Subject: {EncodeHeader(subject)}");
        builder.AppendLine($"Date: {DateTimeOffset.UtcNow:R}");
        builder.AppendLine($"Message-Id: <{Guid.NewGuid():N}@{GetDomain(options.FromEmail)}>");
        builder.AppendLine("Auto-Submitted: auto-generated");
        builder.AppendLine("X-Auto-Response-Suppress: All");
        if (!string.IsNullOrWhiteSpace(options.ReplyToEmail))
        {
            builder.AppendLine($"Reply-To: <{options.ReplyToEmail.Trim()}>");
        }

        builder.AppendLine("MIME-Version: 1.0");
        builder.AppendLine($"Content-Type: multipart/related; boundary=\"{boundary}\"");
        builder.AppendLine();
        builder.AppendLine($"--{boundary}");
        builder.AppendLine("Content-Type: text/html; charset=utf-8");
        builder.AppendLine("Content-Transfer-Encoding: base64");
        builder.AppendLine();
        foreach (var line in SplitBase64(bodyBase64))
        {
            builder.AppendLine(line);
        }

        if (logoBytes is not null)
        {
            builder.AppendLine($"--{boundary}");
            builder.AppendLine("Content-Type: image/png; name=\"fulvero-logo.png\"");
            builder.AppendLine("Content-Transfer-Encoding: base64");
            builder.AppendLine("Content-ID: <fulvero-logo>");
            builder.AppendLine("Content-Disposition: inline; filename=\"fulvero-logo.png\"");
            builder.AppendLine();
            foreach (var line in SplitBase64(Convert.ToBase64String(logoBytes)))
            {
                builder.AppendLine(line);
            }
        }

        builder.AppendLine($"--{boundary}--");

        return builder.ToString();
    }

    private byte[]? LoadLogoBytes(SmtpOptions value)
    {
        var candidates = new[]
        {
            value.LogoFilePath,
            Path.Combine(environment.ContentRootPath, value.LogoFilePath),
            Path.Combine(environment.ContentRootPath, "wwwroot", "email-logo.png"),
            Path.Combine(environment.ContentRootPath, "..", "landing", "assets", "fulvero-logo.png")
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var path = Path.GetFullPath(candidate);
            if (File.Exists(path))
            {
                return File.ReadAllBytes(path);
            }
        }

        logger.LogWarning("Email logo file was not found. Email will be sent without inline logo.");
        return null;
    }

    private static IEnumerable<string> SplitBase64(string value) =>
        Enumerable.Range(0, (value.Length + 75) / 76)
            .Select(index => value.Substring(index * 76, Math.Min(76, value.Length - index * 76)));

    private static string EncodeAddress(MailAddress address) =>
        string.IsNullOrWhiteSpace(address.DisplayName)
            ? $"<{address.Address}>"
            : $"{EncodeHeader(address.DisplayName)} <{address.Address}>";

    private static string EncodeHeader(string value) =>
        $"=?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}?=";

    private static string GetDomain(string email) =>
        email.Contains('@', StringComparison.Ordinal)
            ? email.Split('@').Last()
            : "fulvero.ru";

    private static async Task CommandAsync(
        StreamReader reader,
        StreamWriter writer,
        string command,
        int expectedCode,
        CancellationToken cancellationToken)
    {
        await writer.WriteLineAsync(command.AsMemory(), cancellationToken);
        await ExpectAsync(reader, expectedCode, cancellationToken);
    }

    private static async Task ExpectAsync(StreamReader reader, int expectedCode, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                throw new InvalidOperationException("SMTP server closed the connection.");
            }

            if (line.Length < 3 || !int.TryParse(line[..3], out var code))
            {
                throw new InvalidOperationException($"Invalid SMTP response: {line}");
            }

            if (code != expectedCode)
            {
                throw new InvalidOperationException($"Unexpected SMTP response: {line}");
            }

            if (line.Length < 4 || line[3] != '-')
            {
                return;
            }
        }
    }

    private static string WrapHtml(string content, bool hasInlineLogo)
    {
        var logo = hasInlineLogo
            ? """<img src="cid:fulvero-logo" alt="Fulvero" width="180" style="display:block;width:180px;max-width:100%;height:auto;margin:0 0 22px;">"""
            : """<div style="margin:0 0 22px;font-size:22px;font-weight:700;letter-spacing:.06em;color:#2563eb;">FULVERO</div>""";

        return
        $$"""
        <!doctype html>
        <html lang="ru">
          <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
          </head>
          <body style="margin:0;background:#f4f7fb;color:#111827;font-family:Arial,sans-serif;">
            <div style="max-width:620px;margin:0 auto;padding:28px 16px;">
              <div style="background:#ffffff;border:1px solid #d8e1ef;border-radius:14px;padding:24px;">
                {{logo}}
                {{content}}
              </div>
              <p style="margin:16px 0 0;color:#64748b;font-size:12px;">Fulvero, техническое уведомление.</p>
            </div>
          </body>
        </html>
        """;
    }
}

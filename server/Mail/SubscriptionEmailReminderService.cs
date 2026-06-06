using Fulvero.Api.Data;
using Fulvero.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Fulvero.Api.Mail;

public class SubscriptionEmailReminderService(
    IServiceScopeFactory scopeFactory,
    ILogger<SubscriptionEmailReminderService> logger) : BackgroundService
{
    private readonly HashSet<string> sentKeys = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendRemindersAsync(stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to send subscription reminders.");
            }

            await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
        }
    }

    private async Task SendRemindersAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var emailSender = scope.ServiceProvider.GetRequiredService<EmailSender>();
        var now = DateTimeOffset.UtcNow;
        var today = now.Date.ToString("yyyyMMdd");

        var trialCompanies = await db.Companies
            .AsNoTracking()
            .Where(company => company.SubscriptionStatus == CompanySubscriptionStatuses.Trial
                && company.TrialEndsAt > now
                && company.TrialEndsAt <= now.AddDays(1))
            .ToListAsync(cancellationToken);

        foreach (var company in trialCompanies)
        {
            await SendToAdminsAsync(
                db,
                emailSender,
                company.Id,
                $"trial:{company.Id}:{today}",
                "Триал Fulvero заканчивается завтра",
                $"""
                <h1 style="margin:0 0 12px;font-size:22px;">Триал скоро закончится</h1>
                <p style="font-size:15px;line-height:1.55;">У компании <strong>{Escape(company.Name)}</strong> пробный доступ заканчивается {FormatDate(company.TrialEndsAt)}.</p>
                <p style="font-size:15px;line-height:1.55;">Чтобы команда продолжила работу без остановки, администратор может продлить тариф в настройках Fulvero.</p>
                """,
                cancellationToken);
        }

        var subscriptionCompanies = await db.Companies
            .AsNoTracking()
            .Where(company => company.SubscriptionStatus == CompanySubscriptionStatuses.Active
                && company.SubscriptionPaidUntil != null
                && company.SubscriptionPaidUntil > now
                && company.SubscriptionPaidUntil <= now.AddDays(3))
            .ToListAsync(cancellationToken);

        foreach (var company in subscriptionCompanies)
        {
            var paidUntil = company.SubscriptionPaidUntil;
            if (paidUntil is null)
            {
                continue;
            }

            await SendToAdminsAsync(
                db,
                emailSender,
                company.Id,
                $"subscription:{company.Id}:{today}",
                "Подписка Fulvero скоро закончится",
                $"""
                <h1 style="margin:0 0 12px;font-size:22px;">Подписка скоро закончится</h1>
                <p style="font-size:15px;line-height:1.55;">У компании <strong>{Escape(company.Name)}</strong> оплаченный период действует до {FormatDate(paidUntil.Value)}.</p>
                <p style="font-size:15px;line-height:1.55;">Fulvero использует ручное продление: администратор сам нажимает кнопку оплаты тарифа.</p>
                """,
                cancellationToken);
        }
    }

    private async Task SendToAdminsAsync(
        AppDbContext db,
        EmailSender emailSender,
        Guid companyId,
        string key,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        if (!sentKeys.Add(key))
        {
            return;
        }

        var adminEmails = await db.Users
            .AsNoTracking()
            .Where(user => user.CompanyId == companyId
                && user.Role == UserRoles.Admin
                && user.IsActive
                && user.Email != "")
            .Select(user => user.Email)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var email in adminEmails)
        {
            await emailSender.SendAsync(email, subject, body, cancellationToken);
        }
    }

    private static string Escape(string value) => System.Net.WebUtility.HtmlEncode(value);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToLocalTime().ToString("dd.MM.yyyy");
}

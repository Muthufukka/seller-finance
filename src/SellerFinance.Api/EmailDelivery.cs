using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;

namespace SellerFinance.Api;

public sealed record EmailDeliverySettings(
    string Host,
    int Port,
    bool UseTls,
    string FromAddress,
    string FromName,
    string? User,
    string? Password,
    int TimeoutSeconds)
{
    public static bool TryCreate(IConfiguration configuration, IHostEnvironment environment, out EmailDeliverySettings? settings, out string? error)
    {
        settings = null;
        error = null;
        var host = configuration["EMAIL_SMTP_HOST"]?.Trim();
        var from = configuration["EMAIL_FROM"]?.Trim();
        if (String.IsNullOrWhiteSpace(host) || String.IsNullOrWhiteSpace(from))
        {
            error = "EMAIL_SMTP_HOST and EMAIL_FROM are required";
            return false;
        }

        try { _ = new MailAddress(from); }
        catch (FormatException)
        {
            error = "EMAIL_FROM must be a valid email address";
            return false;
        }

        var port = configuration.GetValue("EMAIL_SMTP_PORT", 587);
        if (port is < 1 or > 65535)
        {
            error = "EMAIL_SMTP_PORT must be between 1 and 65535";
            return false;
        }

        var timeoutSeconds = configuration.GetValue("EMAIL_SMTP_TIMEOUT_SECONDS", 15);
        if (timeoutSeconds is < 1 or > 120)
        {
            error = "EMAIL_SMTP_TIMEOUT_SECONDS must be between 1 and 120";
            return false;
        }

        var user = configuration["EMAIL_SMTP_USER"]?.Trim();
        var password = configuration["EMAIL_SMTP_PASSWORD"];
        if (String.IsNullOrWhiteSpace(user) != String.IsNullOrWhiteSpace(password))
        {
            error = "EMAIL_SMTP_USER and EMAIL_SMTP_PASSWORD must be configured together";
            return false;
        }

        var useTls = configuration.GetValue("EMAIL_SMTP_TLS", true);
        if (environment.IsProduction() && !useTls)
        {
            error = "EMAIL_SMTP_TLS must be enabled in Production";
            return false;
        }

        settings = new(host, port, useTls, from,
            configuration["EMAIL_FROM_NAME"]?.Trim() is { Length: > 0 } name ? name : "Seller Finance",
            user, password, timeoutSeconds);
        return true;
    }
}

public sealed class EmailDelivery
{
    private readonly EmailDeliverySettings? settings;
    private readonly ILogger<EmailDelivery> logger;

    public EmailDelivery(IConfiguration configuration, IHostEnvironment environment, ILogger<EmailDelivery> logger)
    {
        this.logger = logger;
        EmailDeliverySettings.TryCreate(configuration, environment, out settings, out var error);
        ConfigurationError = error;
    }

    public bool IsConfigured => settings is not null;
    public string? ConfigurationError { get; }

    public async Task<bool> SendAsync(string recipient, string subject, string html, CancellationToken ct)
    {
        if (settings is null) return false;
        try
        {
            using var client = new SmtpClient(settings.Host, settings.Port)
            {
                EnableSsl = settings.UseTls,
                Timeout = checked(settings.TimeoutSeconds * 1000),
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false
            };
            if (settings.User is not null)
                client.Credentials = new NetworkCredential(settings.User, settings.Password);

            using var message = new MailMessage
            {
                From = new MailAddress(settings.FromAddress, settings.FromName),
                Subject = subject,
                Body = html,
                IsBodyHtml = true
            };
            message.To.Add(new MailAddress(recipient));
            await client.SendMailAsync(message, ct);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is SmtpException or FormatException or InvalidOperationException)
        {
            logger.LogWarning("Transactional email delivery failed with {ErrorType}", ex.GetType().Name);
            return false;
        }
    }

    public static string ConfirmationHtml(string url) => Layout(
        "Подтвердите email",
        "Подтвердите адрес электронной почты для Seller Finance.",
        "Подтвердить email",
        url,
        "Если вы не регистрировались, просто проигнорируйте это письмо.");

    public static string ResetHtml(string url) => Layout(
        "Сброс пароля",
        "Вы запросили смену пароля Seller Finance.",
        "Сбросить пароль",
        url,
        "Если вы не запрашивали сброс, проигнорируйте это письмо.");

    public static string InvitationHtml(string organizationName, string url) => Layout(
        "Приглашение в Seller Finance",
        $"Вас пригласили в организацию {organizationName}.",
        "Принять приглашение",
        url,
        "Ссылка действует 7 дней.");

    private static string Layout(string title, string lead, string button, string url, string footer)
    {
        var encoder = HtmlEncoder.Default;
        return $"""
            <!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width"></head>
            <body style="margin:0;background:#f4f7f5;font-family:Arial,sans-serif;color:#183126">
              <div style="max-width:560px;margin:32px auto;padding:28px;background:#fff;border-radius:12px">
                <h1 style="font-size:22px;margin:0 0 16px">{encoder.Encode(title)}</h1>
                <p style="line-height:1.5">{encoder.Encode(lead)}</p>
                <p style="margin:28px 0"><a href="{encoder.Encode(url)}" style="background:#176b45;color:#fff;padding:12px 18px;border-radius:8px;text-decoration:none">{encoder.Encode(button)}</a></p>
                <p style="font-size:13px;color:#64746c">{encoder.Encode(footer)}</p>
              </div>
            </body></html>
            """;
    }
}

using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;

namespace SellerFinance.Api;

public sealed class EmailDelivery(IConfiguration configuration,ILogger<EmailDelivery> logger)
{
    public bool IsConfigured=>!String.IsNullOrWhiteSpace(configuration["EMAIL_SMTP_HOST"])&&!String.IsNullOrWhiteSpace(configuration["EMAIL_FROM"]);
    public async Task<bool> SendAsync(string recipient,string subject,string html,CancellationToken ct)
    {
        if(!IsConfigured)return false;
        try
        {
            using var client=new SmtpClient(configuration["EMAIL_SMTP_HOST"],configuration.GetValue("EMAIL_SMTP_PORT",587)){EnableSsl=configuration.GetValue("EMAIL_SMTP_TLS",true)};var user=configuration["EMAIL_SMTP_USER"];var password=configuration["EMAIL_SMTP_PASSWORD"];if(!String.IsNullOrWhiteSpace(user))client.Credentials=new NetworkCredential(user,password);using var message=new MailMessage(configuration["EMAIL_FROM"]!,recipient,subject,html){IsBodyHtml=true};await client.SendMailAsync(message,ct);return true;
        }
        catch(Exception ex){logger.LogWarning("Transactional email failed with {ErrorType}",ex.GetType().Name);return false;}
    }
    public static string ConfirmationHtml(string url)=>$"<p>Подтвердите email Seller Finance:</p><p><a href=\"{HtmlEncoder.Default.Encode(url)}\">Подтвердить email</a></p><p>Если вы не регистрировались, проигнорируйте письмо.</p>";
    public static string ResetHtml(string url)=>$"<p>Для смены пароля Seller Finance откройте ссылку:</p><p><a href=\"{HtmlEncoder.Default.Encode(url)}\">Сбросить пароль</a></p><p>Если вы не запрашивали сброс, проигнорируйте письмо.</p>";
}

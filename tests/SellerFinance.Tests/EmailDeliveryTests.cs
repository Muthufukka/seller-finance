using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SellerFinance.Api;
using System.Net;

namespace SellerFinance.Tests;

public sealed class EmailDeliveryTests
{
    [Fact]
    public void Configuration_Requires_Valid_Address_Port_And_Complete_Credentials()
    {
        Assert.False(Create(new()).IsConfigured);
        Assert.False(Create(new() { ["EMAIL_SMTP_HOST"]="smtp.example", ["EMAIL_FROM"]="invalid" }).IsConfigured);
        Assert.False(Create(new() { ["EMAIL_SMTP_HOST"]="smtp.example", ["EMAIL_FROM"]="mail@example.test", ["EMAIL_SMTP_PORT"]="70000" }).IsConfigured);
        Assert.False(Create(new() { ["EMAIL_SMTP_HOST"]="smtp.example", ["EMAIL_FROM"]="mail@example.test", ["EMAIL_SMTP_USER"]="user" }).IsConfigured);
        Assert.True(Create(new() { ["EMAIL_SMTP_HOST"]="smtp.example", ["EMAIL_FROM"]="mail@example.test", ["EMAIL_SMTP_USER"]="user", ["EMAIL_SMTP_PASSWORD"]="password" }).IsConfigured);
    }

    [Fact]
    public void Production_Requires_Smtp_Tls()
    {
        var delivery=Create(new() { ["EMAIL_SMTP_HOST"]="smtp.example", ["EMAIL_FROM"]="mail@example.test", ["EMAIL_SMTP_TLS"]="false" });
        Assert.False(delivery.IsConfigured);
        Assert.Equal("EMAIL_SMTP_TLS must be enabled in Production",delivery.ConfigurationError);
    }

    [Fact]
    public void Templates_Are_Utf8_Safe_And_Html_Encode_Untrusted_Values()
    {
        var confirmation=EmailDelivery.ConfirmationHtml("https://seller.example/?token=a&next=<x>");
        Assert.Contains("Подтвердите email",WebUtility.HtmlDecode(confirmation));
        Assert.Contains("&amp;",confirmation);
        Assert.DoesNotContain("<x>",confirmation);

        var invitation=EmailDelivery.InvitationHtml("Org <script>","https://seller.example/");
        Assert.Contains("Org &lt;script&gt;",invitation);
        Assert.DoesNotContain("Org <script>",invitation);
        Assert.Contains("charset=\"utf-8\"",invitation);
    }

    private static EmailDelivery Create(Dictionary<string,string?> values)
    {
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new EmailDelivery(configuration,new EnvironmentStub(),NullLogger<EmailDelivery>.Instance);
    }

    private sealed class EnvironmentStub:IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

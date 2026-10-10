using System.Net;
using System.Text;
using System.Text.Json;
using Kimlik.Application.Abstractions;
using Kimlik.Application.Accounts;
using Kimlik.Infrastructure.Sms;
using Microsoft.Extensions.Options;

namespace Kimlik.Infrastructure.Tests.Sms;

/// <summary>Each adapter sends the request its provider documents, and turns a refusal into an error the outbox retries.</summary>
public sealed class SmsSenderTests
{
    private static readonly SmsMessage Message = new("+905321234567", "123456 is your Kimlik sign-in code.");

    [Fact]
    public async Task Netgsm_PostsJsonWithBasicAuthentication()
    {
        var provider = new FakeProvider(HttpStatusCode.OK, """{ "code": "00", "jobid": "17377215342605050417149344", "description": "queued" }""");
        var sender = new NetgsmSmsSender(provider, Options.Create(new NetgsmOptions { Username = "8503020000", Password = "secret", Header = "KIMLIK" }));

        await sender.SendAsync(Message, TestContext.Current.CancellationToken);

        provider.Request!.Method.ShouldBe(HttpMethod.Post);
        provider.Request.RequestUri.ShouldBe(new Uri("https://api.netgsm.com.tr/sms/rest/v2/send"));
        provider.Authorization.ShouldBe($"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes("8503020000:secret"))}");
        var body = JsonDocument.Parse(provider.Body!).RootElement;
        body.GetProperty("msgheader").GetString().ShouldBe("KIMLIK");
        body.GetProperty("iysfilter").GetString().ShouldBe("0");
        var sent = body.GetProperty("messages").EnumerateArray().ShouldHaveSingleItem();
        sent.GetProperty("msg").GetString().ShouldBe(Message.Text);
        sent.GetProperty("no").GetString().ShouldBe("905321234567");
        body.TryGetProperty("encoding", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Netgsm_SendsTurkishCharacters_WithTheirEncoding()
    {
        var provider = new FakeProvider(HttpStatusCode.OK, """{ "code": "00", "jobid": "1" }""");
        var sender = new NetgsmSmsSender(provider, Options.Create(new NetgsmOptions { Username = "user", Password = "secret", Header = "KIMLIK" }));

        await sender.SendAsync(Message with { Text = "Giriş kodunuz: 123456" }, TestContext.Current.CancellationToken);

        JsonDocument.Parse(provider.Body!).RootElement.GetProperty("encoding").GetString().ShouldBe("TR");
    }

    [Fact]
    public async Task Netgsm_Refusal_IsAnError_WithoutTheCredentials()
    {
        var provider = new FakeProvider(HttpStatusCode.NotAcceptable, """{ "code": "30", "description": "Invalid username, password or no API access." }""");
        var sender = new NetgsmSmsSender(provider, Options.Create(new NetgsmOptions { Username = "user", Password = "secret", Header = "KIMLIK" }));

        var error = await Should.ThrowAsync<SmsDeliveryException>(() => sender.SendAsync(Message, TestContext.Current.CancellationToken));

        error.Message.ShouldContain("code 30");
        error.Message.ShouldNotContain("secret");
    }

    [Fact]
    public async Task IletiMerkezi_PostsTheOrderWithTheKeyAndHash()
    {
        var provider = new FakeProvider(HttpStatusCode.OK, """{ "response": { "status": { "code": 200, "message": "İşlem başarılı" }, "order": { "id": "312891245" } } }""");
        var sender = new IletiMerkeziSmsSender(provider, Options.Create(new IletiMerkeziOptions { ApiKey = "key", ApiHash = "hash", Sender = "KIMLIK" }));

        await sender.SendAsync(Message, TestContext.Current.CancellationToken);

        provider.Request!.Method.ShouldBe(HttpMethod.Post);
        provider.Request.RequestUri.ShouldBe(new Uri("https://api.iletimerkezi.com/v1/send-sms/json"));
        var request = JsonDocument.Parse(provider.Body!).RootElement.GetProperty("request");
        request.GetProperty("authentication").GetProperty("key").GetString().ShouldBe("key");
        request.GetProperty("authentication").GetProperty("hash").GetString().ShouldBe("hash");
        var order = request.GetProperty("order");
        order.GetProperty("sender").GetString().ShouldBe("KIMLIK");
        order.GetProperty("iys").GetString().ShouldBe("0");
        order.GetProperty("message").GetProperty("text").GetString().ShouldBe(Message.Text);
        order.GetProperty("message").GetProperty("receipents").GetProperty("number").EnumerateArray().ShouldHaveSingleItem().GetString().ShouldBe("+905321234567");
    }

    [Fact]
    public async Task IletiMerkezi_Refusal_IsAnError()
    {
        var provider = new FakeProvider(HttpStatusCode.Unauthorized, """{ "response": { "status": { "code": 401, "message": "Üyelik bilgileri hatalı" } } }""");
        var sender = new IletiMerkeziSmsSender(provider, Options.Create(new IletiMerkeziOptions { ApiKey = "key", ApiHash = "hash", Sender = "KIMLIK" }));

        var error = await Should.ThrowAsync<SmsDeliveryException>(() => sender.SendAsync(Message, TestContext.Current.CancellationToken));

        error.Message.ShouldContain("status 401");
    }

    [Fact]
    public async Task Twilio_PostsAFormWithBasicAuthentication()
    {
        var provider = new FakeProvider(HttpStatusCode.Created, """{ "sid": "SM1", "status": "queued" }""");
        var sender = new TwilioSmsSender(provider, Options.Create(new TwilioOptions { AccountSid = "AC123", AuthToken = "token", From = "+15017122661" }));

        await sender.SendAsync(Message, TestContext.Current.CancellationToken);

        provider.Request!.Method.ShouldBe(HttpMethod.Post);
        provider.Request.RequestUri.ShouldBe(new Uri("https://api.twilio.com/2010-04-01/Accounts/AC123/Messages.json"));
        provider.Authorization.ShouldBe($"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes("AC123:token"))}");
        provider.ContentType.ShouldBe("application/x-www-form-urlencoded");
        var form = System.Web.HttpUtility.ParseQueryString(provider.Body!);
        form["To"].ShouldBe("+905321234567");
        form["From"].ShouldBe("+15017122661");
        form["Body"].ShouldBe(Message.Text);
    }

    [Fact]
    public async Task Twilio_UsesAMessagingService_WhenOneIsSet()
    {
        var provider = new FakeProvider(HttpStatusCode.Created, """{ "sid": "SM1", "status": "accepted" }""");
        var sender = new TwilioSmsSender(provider, Options.Create(new TwilioOptions { AccountSid = "AC123", AuthToken = "token", MessagingServiceSid = "MG123" }));

        await sender.SendAsync(Message, TestContext.Current.CancellationToken);

        var form = System.Web.HttpUtility.ParseQueryString(provider.Body!);
        form["MessagingServiceSid"].ShouldBe("MG123");
        form["From"].ShouldBeNull();
    }

    [Fact]
    public async Task Twilio_Refusal_IsAnError()
    {
        var provider = new FakeProvider(HttpStatusCode.BadRequest, """{ "code": 21211, "message": "The 'To' number is not a valid phone number.", "status": 400 }""");
        var sender = new TwilioSmsSender(provider, Options.Create(new TwilioOptions { AccountSid = "AC123", AuthToken = "token", From = "+15017122661" }));

        var error = await Should.ThrowAsync<SmsDeliveryException>(() => sender.SendAsync(Message, TestContext.Current.CancellationToken));

        error.Message.ShouldContain("code 21211");
        error.Message.ShouldNotContain("token");
    }

    [Fact]
    public void ChosenProvider_WithoutItsSettings_FailsAtStartup()
    {
        var validator = new ValidateSmsProvider(
            Options.Create(new NetgsmOptions()), Options.Create(new IletiMerkeziOptions()), Options.Create(new TwilioOptions { AccountSid = "AC123" }));

        var result = validator.Validate(null, new SmsOptions { Provider = SmsProvider.Twilio });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Kimlik:Sms:Twilio:AuthToken");
        result.FailureMessage.ShouldNotContain("AccountSid");
        validator.Validate(null, new SmsOptions()).Succeeded.ShouldBeTrue();
    }

    /// <summary>Answers every request as the provider would, and keeps the request.</summary>
    private sealed class FakeProvider(HttpStatusCode status, string answer) : HttpMessageHandler, IHttpClientFactory
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        public string? Authorization { get; private set; }

        public string? ContentType { get; private set; }

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Authorization = request.Headers.Authorization?.ToString();
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
        }
    }
}

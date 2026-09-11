using System.Net;
using System.Text;
using Ground43.Api.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Ground43.Api.Tests;

public sealed class WeComNotifierTests
{
    [Fact]
    public async Task LookupUserByEmailAsync_UsesEmailLookupEndpoint()
    {
        var requestedPaths = new List<string>();
        using var http = new HttpClient(new StubHttpMessageHandler(async request =>
        {
            requestedPaths.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath == "/cgi-bin/user/get_userid_by_email")
            {
                using var body = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.Equal(2, body.RootElement.GetProperty("email_type").GetInt32());
            }
            return request.RequestUri.AbsolutePath switch
            {
                "/cgi-bin/gettoken" => Json("""{"errcode":0,"errmsg":"ok","access_token":"token","expires_in":7200}"""),
                "/cgi-bin/user/get_userid_by_email" => Json("""{"errcode":0,"errmsg":"ok","userid":"demo-user-id"}"""),
                "/cgi-bin/user/get" => Json("""{"errcode":0,"errmsg":"ok","userid":"demo-user-id","name":"admin","department":[1],"status":1}"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }));
        var notifier = CreateNotifier(http);

        var profile = await notifier.LookupUserByEmailAsync("member@example.com", CancellationToken.None);

        Assert.Equal("demo-user-id", profile.UserId);
        Assert.Contains("/cgi-bin/user/get_userid_by_email", requestedPaths);
        Assert.DoesNotContain("/cgi-bin/user/get_userid", requestedPaths);
    }

    [Fact]
    public async Task LookupUserByEmailAsync_IncludesHttpStatusWhenRequestFails()
    {
        using var http = new HttpClient(new StubHttpMessageHandler(request => Task.FromResult(
            request.RequestUri!.AbsolutePath == "/cgi-bin/gettoken"
                ? Json("""{"errcode":0,"errmsg":"ok","access_token":"token","expires_in":7200}""")
                : new HttpResponseMessage(HttpStatusCode.NotFound))));
        var notifier = CreateNotifier(http);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            notifier.LookupUserByEmailAsync("member@example.com", CancellationToken.None));

        Assert.Contains("HTTP 404", exception.Message);
    }

    [Theory]
    [InlineData("{\"errcode\":0,\"errmsg\":\"ok\",\"invaliduser\":\"wx-chen\"}")]
    [InlineData("{\"errcode\":0,\"errmsg\":\"ok\",\"unlicenseduser\":\"wx-chen\"}")]
    public async Task Send_RejectsUndeliverableRecipientEvenWithErrcodeZero(string sendResult)
    {
        using var http = new HttpClient(new StubHttpMessageHandler(request => Task.FromResult(
            request.RequestUri!.AbsolutePath == "/cgi-bin/gettoken"
                ? Json("""{"errcode":0,"access_token":"token","expires_in":7200}""") : Json(sendResult))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateNotifier(http).SendToUserAsync("wx-chen", "测试", default));
    }

    private static WeComNotifier CreateNotifier(HttpClient http) => new(
        http,
        Options.Create(new WeComOptions { CorpId = "test-corp", Secret = "test-secret", AgentId = "1" }),
        NullLogger<WeComNotifier>.Instance);

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}

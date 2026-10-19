using System.Net;
using Arqon.Core.Updates;

namespace Arqon.Core.Tests.Updates;

public class UpdateCheckerTests
{
    private const string Good = """{"tag_name":"v0.2.0","name":"Arqon 0.2.0","html_url":"https://github.com/shaxboz1011/Arqon/releases/tag/v0.2.0","draft":false,"prerelease":false}""";

    [Fact]
    public void A_normal_release_is_read()
    {
        var release = UpdateChecker.ParseLatest(Good);

        Assert.Equal(new ReleaseInfo("v0.2.0", "https://github.com/shaxboz1011/Arqon/releases/tag/v0.2.0", "Arqon 0.2.0"), release);
    }

    [Theory]
    [InlineData("""{"tag_name":"v0.2.0","html_url":"https://github.com/shaxboz1011/Arqon/releases/tag/v0.2.0","draft":true}""")]
    [InlineData("""{"tag_name":"v0.2.0","html_url":"https://github.com/shaxboz1011/Arqon/releases/tag/v0.2.0","prerelease":true}""")]
    [InlineData("""{"name":"no tag","html_url":"https://github.com/shaxboz1011/Arqon/releases/tag/x"}""")]
    [InlineData("""{"tag_name":"v9","html_url":"https://evil.example/shaxboz1011/Arqon/releases/tag/v9"}""")]
    [InlineData("""{"tag_name":"v9","html_url":"https://github.com/someone-else/Arqon/releases/tag/v9"}""")]
    [InlineData("""{"tag_name":"v9"}""")]
    [InlineData("""["not an object"]""")]
    [InlineData("not json at all")]
    [InlineData("")]
    public void Anything_that_is_not_a_published_release_of_this_project_is_ignored(string json)
    {
        Assert.Null(UpdateChecker.ParseLatest(json));
    }

    [Theory]
    [InlineData("0.1.3", "v0.1.4", true)]
    [InlineData("0.1.3", "v0.2.0", true)]
    [InlineData("0.1.3", "v1.0.0", true)]
    [InlineData("0.1.3", "v0.1.3", false)]
    [InlineData("0.1.3", "v0.1.2", false)]
    [InlineData("0.2.0", "v0.1.9", false)]
    [InlineData("0.1.9", "v0.1.10", true)]
    [InlineData("0.1.3-beta", "v0.1.3", false)]
    [InlineData("0.1.3+abc123", "v0.1.4", true)]
    [InlineData("0.1.3", "0.1.4", true)]
    [InlineData("1.0", "v1.0.1", true)]
    [InlineData("0.1.3", "nightly", false)]
    [InlineData("garbage", "v1.0.0", false)]
    public void Versions_are_compared_number_by_number(string current, string tag, bool newer)
    {
        Assert.Equal(newer, UpdateChecker.IsNewer(current, tag));
    }

    private sealed class Stub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Seen;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task The_request_goes_to_the_projects_latest_release_and_names_itself()
    {
        var stub = new Stub(HttpStatusCode.OK, Good);

        var release = await UpdateChecker.LatestAsync(new HttpClient(stub));

        Assert.Equal("v0.2.0", release!.Tag);
        Assert.Equal("https://api.github.com/repos/shaxboz1011/Arqon/releases/latest", stub.Seen!.RequestUri!.ToString());
        Assert.Contains("Arqon", stub.Seen.Headers.UserAgent.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task An_error_answer_means_no_news(HttpStatusCode status)
    {
        Assert.Null(await UpdateChecker.LatestAsync(new HttpClient(new Stub(status, "{}"))));
    }

    [Fact]
    public async Task No_connection_means_no_news_instead_of_an_error()
    {
        var down = new HttpClient(new FailingHandler());

        Assert.Null(await UpdateChecker.LatestAsync(down));
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("offline");
    }
}

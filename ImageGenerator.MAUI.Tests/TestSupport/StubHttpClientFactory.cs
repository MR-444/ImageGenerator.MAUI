namespace ImageGenerator.MAUI.Tests.TestSupport;

// Hands the same HttpClient (built once around a test handler) back from CreateClient
// regardless of name. Fixtures own the HttpClient lifetime; this stub is just the
// production-side seam that the services consume now.
internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly Func<HttpClient> _build;

    public StubHttpClientFactory(Func<HttpClient> build) => _build = build;

    public StubHttpClientFactory(HttpClient client) : this(() => client) { }

    public HttpClient CreateClient(string name) => _build();
}

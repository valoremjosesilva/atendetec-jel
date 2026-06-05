namespace Atendefy.Tests.Helpers;

public class MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(handler(request));
    }

    public static MockHttpMessageHandler ReturnsJson(string json, int statusCode = 200)
        => new(_ => new HttpResponseMessage((System.Net.HttpStatusCode)statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
}

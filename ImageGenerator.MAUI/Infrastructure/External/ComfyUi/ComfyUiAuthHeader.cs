namespace ImageGenerator.MAUI.Infrastructure.External.ComfyUi;

/// <summary>
/// Applies the user's optional ComfyUI Authorization header (a FULL header value like
/// "Bearer abc123") to the per-run HttpClient. TryAddWithoutValidation because the value
/// is user-typed and may use a scheme HttpHeaders would reject; whitespace-only = LAN
/// setup, no header.
/// </summary>
internal static class ComfyUiAuthHeader
{
    public const string HeaderName = "Authorization";

    public static void Apply(HttpClient client, string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue)) return;
        client.DefaultRequestHeaders.TryAddWithoutValidation(HeaderName, headerValue.Trim());
    }
}

namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Owns SecureStorage interactions for the OpenRouter API key. Used by remote vision observation
/// in "Describe an idea" when the image source is routed through OpenRouter.
/// </summary>
public interface IOpenRouterTokenStore : ITokenStore
{
}

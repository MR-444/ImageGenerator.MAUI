namespace ImageGenerator.MAUI.Models;

public abstract class ImageModelBase
{
    public virtual required string ModelName { get; set; }
    public virtual string ApiToken { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
}
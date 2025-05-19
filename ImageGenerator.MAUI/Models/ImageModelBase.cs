namespace ImageGenerator.MAUI.Models;

public abstract class ImageModelBase
{
    public abstract required string ModelName { get; set; }
    public virtual string Prompt { get; set; } = string.Empty;
}
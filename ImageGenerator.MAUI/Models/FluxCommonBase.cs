namespace ImageGenerator.MAUI.Models;

public abstract class FluxCommonBase
{
    public int? Seed { get; set; }

    public virtual string Prompt { get; set; }

    public virtual string AspectRatio { get; set; } = "1:1";

    public virtual string OutputFormat { get; set; } = "png";

    public virtual int SafetyTolerance { get; set; } = 6;
}

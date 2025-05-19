namespace ImageGenerator.MAUI.Models.Flux;

public class FluxCannyPro : FluxBase
{
    public override required string ModelName
    {
        get => "flux-canny-pro";
        set => throw new NotImplementedException();
    }

    public int Steps { get; set; } = 50;

    public double Guidance { get; set; } = 30;

    public string? ControlImage { get; set; }

    public bool PromptUpsampling { get; set; } = false;
}

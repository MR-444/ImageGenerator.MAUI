namespace ImageGenerator.MAUI.Models;

public class GeneratedImage
{
    public string? Message { get; set; }
    public string? FilePath { get; set; }
    
    public string? ImageDataBase64 { get; set; } // now safe across ABI  
    
    public long UpdatedSeed { get; set; }

}
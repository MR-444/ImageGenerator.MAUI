namespace ImageGenerator.MAUI.Services;

public interface IImageGenerationServiceFactory
{
    IImageGenerationService GetService(string modelName);
} 
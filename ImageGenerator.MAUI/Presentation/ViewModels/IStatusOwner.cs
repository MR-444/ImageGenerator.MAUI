namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// Implemented by any ViewModel that drives a status label. Exists so the shared
/// <c>StatusLabel</c> style's colour <c>DataTrigger</c>s can name a single <c>x:DataType</c>
/// (<c>vm:IStatusOwner</c>) and therefore compile, instead of falling back to reflection
/// bindings (which emit XamlC XC0022). The four page ViewModels already expose a
/// <see cref="StatusKind"/> observable property, so implementing this is declaration-only.
/// </summary>
public interface IStatusOwner
{
    StatusKind StatusKind { get; }
}

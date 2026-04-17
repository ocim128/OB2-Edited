namespace Flux.Native.Services;

/// <summary>
/// Interface for pages that support updating their view model state when navigated to.
/// Replaces reflection-based UpdateViewModel calls in NavigationService.
/// </summary>
public interface IUpdatablePage
{
    void UpdateViewModel();
}

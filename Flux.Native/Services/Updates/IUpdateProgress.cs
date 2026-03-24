namespace Flux.Native.Services;

internal interface IUpdateProgress
{
    void Show();
    void Report(double percent, string message);
    void SetIndeterminate(bool isIndeterminate);
    void Close();
    bool IsVisible { get; }
}

using System.Windows;

namespace Flux.Native.ViewModels.Configs;

internal sealed class ConfigStackerClipboardAdapter
{
    public bool TrySetText(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryGetText(out string text)
    {
        text = string.Empty;

        try
        {
            if (!Clipboard.ContainsText())
            {
                return false;
            }

            text = Clipboard.GetText();
            return !string.IsNullOrWhiteSpace(text);
        }
        catch
        {
            return false;
        }
    }
}

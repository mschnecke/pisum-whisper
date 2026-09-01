namespace Pisum.Whisper.App.Settings.Views;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

public sealed partial class HotkeyView : UserControl
{
    public HotkeyView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

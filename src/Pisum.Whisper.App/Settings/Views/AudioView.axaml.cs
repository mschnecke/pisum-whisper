namespace Pisum.Whisper.App.Settings.Views;

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

public sealed partial class AudioView : UserControl
{
    public AudioView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

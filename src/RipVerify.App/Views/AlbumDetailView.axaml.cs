using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RipVerify.App.Views;

public partial class AlbumDetailView : UserControl
{
    public AlbumDetailView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

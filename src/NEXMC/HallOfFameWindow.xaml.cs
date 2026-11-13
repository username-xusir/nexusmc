using System.Windows;
using System.Windows.Input;
using NEXMC.Services;

namespace NEXMC;

public partial class HallOfFameWindow : Wpf.Ui.Controls.FluentWindow
{
    public HallOfFameWindow()
    {
        InitializeComponent();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string url } ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        ExternalBrowser.Open(uri.AbsoluteUri, this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

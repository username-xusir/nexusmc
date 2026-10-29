using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

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

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // 打不开浏览器时不影响鸣谢页继续使用。
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

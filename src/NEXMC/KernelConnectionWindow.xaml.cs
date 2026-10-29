using System.Windows;

namespace NEXMC;

public partial class KernelConnectionWindow : Wpf.Ui.Controls.FluentWindow
{
    public KernelConnectionWindow(string details)
    {
        InitializeComponent();
        DetailsText.Text = details;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

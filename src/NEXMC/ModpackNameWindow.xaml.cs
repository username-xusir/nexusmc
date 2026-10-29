using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NEXMC.Services;

namespace NEXMC;

public partial class ModpackNameWindow : Wpf.Ui.Controls.FluentWindow
{
    public ModpackNameWindow(string suggestedName)
    {
        InitializeComponent();
        NameTextBox.Text = suggestedName;
        UpdateValidation();
    }

    public string InstanceName { get; private set; } = string.Empty;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        NameTextBox.Focus();
        NameTextBox.SelectAll();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void NameTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateValidation();

    private void UpdateValidation()
    {
        if (ValidationText is null) return;
        var error = InstallInputValidator.GetInstanceNameError(NameTextBox.Text);
        if (error is null)
        {
            ValidationText.Text = "名称可用。";
            ValidationText.Foreground = (Brush)FindResource("MutedBrush");
        }
        else
        {
            ValidationText.Text = "名称不可用：" + error;
            ValidationText.Foreground = (Brush)FindResource("DangerBrush");
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        var error = InstallInputValidator.GetInstanceNameError(NameTextBox.Text);
        if (error is not null)
        {
            UpdateValidation();
            NameTextBox.Focus();
            return;
        }

        InstanceName = InstallInputValidator.ValidateInstanceName(NameTextBox.Text);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

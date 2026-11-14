using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ZhccCargoBoard.Desktop.Views;

public partial class DetailWindow : Window
{
    public DetailWindow() => InitializeComponent();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}

using System.Windows;
using System.Windows.Controls;
using ASD4G.ViewModels;

namespace ASD4G;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void ResolutionPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox ||
            comboBox.SelectedItem is not ComboBoxItem selectedItem ||
            selectedItem.Tag is not string preset ||
            string.IsNullOrWhiteSpace(preset))
        {
            return;
        }

        if (DataContext is MainViewModel viewModel &&
            viewModel.ApplyPresetCommand.CanExecute(preset))
        {
            viewModel.ApplyPresetCommand.Execute(preset);
        }

        comboBox.SelectedIndex = 0;
    }
}

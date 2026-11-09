using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using RipVerify.App.ViewModels;

namespace RipVerify.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, DropHandler);
        AddHandler(DragDrop.DragOverEvent, DragOverHandler);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            _ = OpenFolderAsync(vm);
        }
        else if (e.Key == Key.OemComma && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            _ = OpenSettingsAsync();
        }
        else if (e.Key == Key.F5)
        {
            e.Handled = true;
            vm.ReverifySelectedCommand.Execute(null);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (vm.IsDetailVisible)
            {
                vm.BackToDashboardCommand.Execute(null);
            }
        }
    }

    private static void DragOverHandler(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
    }

    private void DropHandler(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var paths = new List<string>();
        var files = e.DataTransfer.TryGetFiles();
        if (files is not null)
        {
            foreach (var file in files)
            {
                var path = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                {
                    paths.Add(path);
                }
            }
        }

        vm.AddDroppedPaths(paths);
    }

    private async void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await OpenFolderAsync(vm);
        }
    }

    private async void DropArea_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            await OpenFolderAsync(vm);
        }
    }

    private async void OpenSettings_Click(object? sender, RoutedEventArgs e)
    {
        await OpenSettingsAsync();
    }

    private void BatchList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainWindowViewModel vm && vm.SelectedItem is not null)
        {
            vm.OpenDetailCommand.Execute(vm.SelectedItem);
            e.Handled = true;
        }
    }

    private async Task OpenFolderAsync(MainWindowViewModel vm)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = vm.OpenFolderText,
            AllowMultiple = true
        });

        var paths = folders
            .Select(folder => folder.TryGetLocalPath())
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
            .ToList();

        vm.AddDroppedPaths(paths);
    }

    private async Task OpenSettingsAsync()
    {
        if (Application.Current is not global::RipVerify.App.App app || app.ServiceProvider is null)
        {
            return;
        }

        var viewModel = app.ServiceProvider.GetRequiredService<SettingsViewModel>();
        var window = new SettingsView { DataContext = viewModel };
        await window.ShowDialog(this);
    }
}

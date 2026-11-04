using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using FactoryNodes.MapNodes.Presentation;

namespace MapNodes;

/// <summary>Solo pegamento: el ViewModel, Ctrl+S, soltar un fichero en la ventana, el menú del idioma y preguntar al cerrar con cambios.</summary>
public partial class MainWindow : Window
{
    private readonly MapNodesViewModel _model;

    public MainWindow(WpfMapNodesServices services, string? file)
    {
        InitializeComponent();
        services.Owner = this;
        _model = new MapNodesViewModel(services);
        DataContext = _model;
        InputBindings.Add(new KeyBinding(_model.SaveCommand, Key.S, ModifierKeys.Control));
        Drop += OnDrop;
        if (file is not null) _model.OpenFile(file);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files &&
            (!_model.IsDirty || MessageBox.Show(this, MapNodesViewModel.OpenQuestion, MapNodesViewModel.ProductName,
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes))
            _model.OpenFile(files[0]);
    }

    private void OnLanguageClick(object sender, RoutedEventArgs e)
    {
        LanguageButton.ContextMenu!.PlacementTarget = LanguageButton;
        LanguageButton.ContextMenu.DataContext = _model;
        LanguageButton.ContextMenu.IsOpen = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_model.IsDirty && MessageBox.Show(this, MapNodesViewModel.CloseQuestion, MapNodesViewModel.ProductName,
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            e.Cancel = true;
        base.OnClosing(e);
    }
}

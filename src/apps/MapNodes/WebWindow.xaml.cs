using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using FactoryNodes.MapNodes.Presentation;

namespace MapNodes;

/// <summary>Solo pegamento: la lista de «Abrir de la web». Se cierra cuando el ViewModel deja de tenerla abierta (al abrir uno o con «Cerrar»).</summary>
public partial class WebWindow : Window
{
    private readonly MapNodesWebViewModel _web;

    public WebWindow(MapNodesWebViewModel web)
    {
        InitializeComponent();
        DataContext = _web = web;
        web.PropertyChanged += OnChanged;
        Closed += (_, _) =>
        {
            web.PropertyChanged -= OnChanged;
            web.IsOpen = false;
        };
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapNodesWebViewModel.IsOpen) && !_web.IsOpen) Close();
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_web.OpenCommand.CanExecute(null)) _web.OpenCommand.Execute(null);
    }
}

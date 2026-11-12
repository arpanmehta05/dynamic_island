using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland.Views;

/// <summary>Three "working" dots that pulse in turn. Only animates while <see cref="Active"/>.</summary>
public partial class DotsControl : UserControl
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(DotsControl), new PropertyMetadata(Brushes.White));
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(nameof(Active), typeof(bool), typeof(DotsControl),
        new PropertyMetadata(false, (d, _) => ((DotsControl)d).Apply()));

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }

    public DotsControl()
    {
        InitializeComponent();
        Loaded += (_, _) => Apply();
        Unloaded += (_, _) => ((Storyboard)Resources["Pulse"]).Stop(this);
    }

    void Apply()
    {
        if (!IsLoaded) return;
        var sb = (Storyboard)Resources["Pulse"];
        sb.Stop(this);
        if (Active) sb.Begin(this, true);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland.Views;

/// <summary>Five bars that dance while <see cref="Active"/>; still (and free) otherwise.</summary>
public partial class EqualizerControl : UserControl
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(EqualizerControl), new PropertyMetadata(Brushes.White));
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(nameof(Active), typeof(bool), typeof(EqualizerControl),
        new PropertyMetadata(false, (d, _) => ((EqualizerControl)d).Apply()));

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }

    public EqualizerControl()
    {
        InitializeComponent();
        Loaded += (_, _) => Apply();
        Unloaded += (_, _) => ((Storyboard)Resources["Dance"]).Stop(this);
    }

    void Apply()
    {
        if (!IsLoaded) return;
        var dance = (Storyboard)Resources["Dance"];
        dance.Stop(this);
        if (Active) dance.Begin(this, true);
    }
}

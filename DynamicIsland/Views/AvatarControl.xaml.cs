using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace DynamicIsland.Views;

/// <summary>The island's own avatar, shown when nothing else has something to say. Still by default; bobs and blinks only while <see cref="Active"/> and visible.</summary>
public partial class AvatarControl : UserControl
{
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(nameof(Active), typeof(bool), typeof(AvatarControl),
        new PropertyMetadata(false, (d, _) => ((AvatarControl)d).Apply()));

    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }

    public AvatarControl()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => Apply();
    }

    void Apply()
    {
        var alive = (Storyboard)Resources["Alive"];
        alive.Stop(this);
        if (Active && IsVisible) alive.Begin(this, true);
    }
}

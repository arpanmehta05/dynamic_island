using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland.Views;

/// <summary>
/// A level bar with its percentage. While <see cref="Run"/> it fills up from zero (the number counts up with it) and, if
/// <see cref="Charging"/>, a glint travels along the fill. Nothing animates while it isn't running.
/// </summary>
public partial class LevelBarControl : UserControl
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(nameof(Percent), typeof(int), typeof(LevelBarControl), new PropertyMetadata(0, (d, _) => ((LevelBarControl)d).Update()));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(LevelBarControl), new PropertyMetadata(Brushes.White, (d, e) => ((LevelBarControl)d).FillRect.Fill = (Brush)e.NewValue));
    public static readonly DependencyProperty ChargingProperty = DependencyProperty.Register(nameof(Charging), typeof(bool), typeof(LevelBarControl), new PropertyMetadata(false, (d, _) => ((LevelBarControl)d).Update()));
    public static readonly DependencyProperty RunProperty = DependencyProperty.Register(nameof(Run), typeof(bool), typeof(LevelBarControl), new PropertyMetadata(false, (d, _) => ((LevelBarControl)d).Restart()));
    static readonly DependencyProperty ShownProperty = DependencyProperty.Register(nameof(Shown), typeof(double), typeof(LevelBarControl), new PropertyMetadata(0.0, (d, e) => ((LevelBarControl)d).Draw((double)e.NewValue)));

    public int Percent { get => (int)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public bool Charging { get => (bool)GetValue(ChargingProperty); set => SetValue(ChargingProperty, value); }
    public bool Run { get => (bool)GetValue(RunProperty); set => SetValue(RunProperty, value); }
    double Shown { get => (double)GetValue(ShownProperty); set => SetValue(ShownProperty, value); }

    public LevelBarControl()
    {
        InitializeComponent();
        FillRect.Fill = Fill;
        Unloaded += (_, _) => Stop();
    }

    void Draw(double value)
    {
        FillScale.ScaleX = Math.Clamp(value, 0, 100) / 100;
        PercentText.Text = $"{Math.Round(value)}%";
    }

    void Restart()
    {
        Stop();
        if (!Run) return;
        BeginAnimation(ShownProperty, null);
        Shown = 0;
        Update();
    }

    /// <summary>Fills to the current level (from wherever it is now) and runs or stops the glint.</summary>
    void Update()
    {
        if (!Run) return;
        BeginAnimation(ShownProperty, new DoubleAnimation(Shown, Percent, TimeSpan.FromSeconds(1.2)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        var glint = (Storyboard)Resources["Glint"];
        glint.Stop(this);
        GlintRect.Opacity = Charging ? 1 : 0;
        if (Charging) glint.Begin(this, true);
    }

    void Stop()
    {
        ((Storyboard)Resources["Glint"]).Stop(this);
        GlintRect.Opacity = 0;
    }
}

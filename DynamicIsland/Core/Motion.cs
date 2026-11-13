using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace DynamicIsland.Core;

/// <summary>
/// A damped spring as an easing curve: it runs past the target a little and settles, the way the island on a phone does.
/// Damping 1 = no overshoot, lower = bouncier; Frequency = how fast it gets there (it must have settled by the end of the animation).
/// </summary>
class SpringEase : EasingFunctionBase
{
    public static readonly DependencyProperty DampingProperty = DependencyProperty.Register(nameof(Damping), typeof(double), typeof(SpringEase), new PropertyMetadata(0.72));
    public static readonly DependencyProperty FrequencyProperty = DependencyProperty.Register(nameof(Frequency), typeof(double), typeof(SpringEase), new PropertyMetadata(13.0));

    public SpringEase() => EasingMode = EasingMode.EaseIn; // the curve below already is the whole motion

    public double Damping { get => (double)GetValue(DampingProperty); set => SetValue(DampingProperty, value); }
    public double Frequency { get => (double)GetValue(FrequencyProperty); set => SetValue(FrequencyProperty, value); }

    protected override double EaseInCore(double t)
    {
        if (t >= 1) return 1;
        double z = Math.Min(Damping, 0.999), w = Frequency, wd = w * Math.Sqrt(1 - z * z);
        return 1 - Math.Exp(-z * w * t) * (Math.Cos(wd * t) + z * w / wd * Math.Sin(wd * t));
    }

    protected override Freezable CreateInstanceCore() => new SpringEase();
}

static class Motion
{
    /// <summary>New content arrives out of a blur that clears as it settles. The effect is removed afterwards, so it costs nothing at rest.</summary>
    public static void BlurIn(UIElement element, double radius = 14, double seconds = 0.38)
    {
        var blur = new BlurEffect { Radius = radius, RenderingBias = RenderingBias.Performance };
        var clear = new DoubleAnimation(0, TimeSpan.FromSeconds(seconds)) { EasingFunction = new CubicEase() };
        clear.Completed += (_, _) => { if (ReferenceEquals(element.Effect, blur)) element.Effect = null; };
        element.Effect = blur;
        blur.BeginAnimation(BlurEffect.RadiusProperty, clear);
    }
}

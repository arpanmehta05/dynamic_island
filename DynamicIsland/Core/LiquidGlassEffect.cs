using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;

namespace DynamicIsland.Core;

/// <summary>Draws the island's glass from the backdrop picture: frost, bent edges, rim light and the shadow underneath. Bounds/Radius follow the animated glass shape.</summary>
class LiquidGlassEffect : ShaderEffect
{
    static readonly PixelShader Shader = new() { UriSource = new Uri("pack://application:,,,/Shaders/LiquidGlass.ps") };

    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(LiquidGlassEffect), 0);
    public static readonly DependencyProperty BoundsProperty = DependencyProperty.Register(nameof(Bounds), typeof(Point4D), typeof(LiquidGlassEffect), new UIPropertyMetadata(new Point4D(), PixelShaderConstantCallback(0)));
    public static readonly DependencyProperty ImageSizeProperty = DependencyProperty.Register(nameof(ImageSize), typeof(Size), typeof(LiquidGlassEffect), new UIPropertyMetadata(new Size(1, 1), PixelShaderConstantCallback(1)));
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(nameof(Radius), typeof(double), typeof(LiquidGlassEffect), new UIPropertyMetadata(0.0, PixelShaderConstantCallback(2)));
    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(nameof(Tint), typeof(double), typeof(LiquidGlassEffect), new UIPropertyMetadata(0.6, PixelShaderConstantCallback(3)));

    public LiquidGlassEffect()
    {
        PixelShader = Shader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(BoundsProperty);
        UpdateShaderValue(ImageSizeProperty);
        UpdateShaderValue(RadiusProperty);
        UpdateShaderValue(TintProperty);
    }

    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public Point4D Bounds { get => (Point4D)GetValue(BoundsProperty); set => SetValue(BoundsProperty, value); }
    public Size ImageSize { get => (Size)GetValue(ImageSizeProperty); set => SetValue(ImageSizeProperty, value); }
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    public double Tint { get => (double)GetValue(TintProperty); set => SetValue(TintProperty, value); }
}

/// <summary>Glass rect in window coordinates -> shader bounds in backdrop-image coordinates (the image is padded on the left).</summary>
class RectToBoundsConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var r = (Rect)value;
        return new Point4D(r.X + Backdrop.Pad, r.Y, r.Width, r.Height);
    }

    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

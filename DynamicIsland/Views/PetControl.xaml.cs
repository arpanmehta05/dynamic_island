using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DynamicIsland.Modules.Agents;

namespace DynamicIsland.Views;

/// <summary>
/// A small pixel creature that reflects what an assistant is doing. Drawn once from a text map; the animations live in the XAML
/// and only run while <see cref="Active"/> (the island is expanded and showing it).
/// </summary>
public partial class PetControl : UserControl
{
    const int P = 4; // pixels per map cell

    // B body, E eye, A arm (left half / right half), L leg, K keyboard
    static readonly string[] Map =
    [
        "...BBBBBBBB...",
        "..BBBBBBBBBB..",
        "AABBBBBBBBBBAA",
        "AABBEBBBBEBBAA",
        "..BBBBBBBBBB..",
        "..BBBBBBBBBB..",
        "...BBBBBBBB...",
        "...L.L..L.L...",
        "..KKKKKKKKKK..",
    ];

    static readonly Brush BodyBrush = Frozen("#E58A66"), ArmBrush = Frozen("#D97757"), LegBrush = Frozen("#A8553A"),
        EyeBrush = Frozen("#2A1710"), KeyBrush = Frozen("#9AA1A9");

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(PetState), typeof(PetControl),
        new PropertyMetadata(PetState.Idle, (d, _) => ((PetControl)d).Apply()));
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(nameof(Active), typeof(bool), typeof(PetControl),
        new PropertyMetadata(false, (d, _) => ((PetControl)d).Apply()));

    /// <summary>Animation frames per second: the big pet runs at 20, the little one in the notch at 10 to stay cheap.</summary>
    public static readonly DependencyProperty FrameRateProperty = DependencyProperty.Register(nameof(FrameRate), typeof(int), typeof(PetControl),
        new PropertyMetadata(20, (d, _) => ((PetControl)d).Apply()));

    public PetState State { get => (PetState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }
    public int FrameRate { get => (int)GetValue(FrameRateProperty); set => SetValue(FrameRateProperty, value); }

    public PetControl()
    {
        InitializeComponent();
        Build();
        Loaded += (_, _) => Apply();
        Unloaded += (_, _) => StopAll();
    }

    void Build()
    {
        for (var r = 0; r < Map.Length; r++)
            for (var c = 0; c < Map[r].Length; c++)
            {
                var (panel, brush) = Map[r][c] switch
                {
                    'B' => (Body, BodyBrush),
                    'E' => (Eyes, EyeBrush),
                    'A' => (c < Map[r].Length / 2 ? ArmL : ArmR, ArmBrush),
                    'L' => (Legs, LegBrush),
                    'K' => (Keyboard, KeyBrush),
                    _ => (null!, null!)
                };
                if (panel is null) continue;
                var px = new Rectangle { Width = P, Height = P, Fill = brush };
                Canvas.SetLeft(px, c * P);
                Canvas.SetTop(px, r * P);
                panel.Children.Add(px);
            }
    }

    void StopAll()
    {
        foreach (var key in new[] { "Idle", "Working", "Sleeping" }) ((Storyboard)Resources[key]).Stop(this);
    }

    void Apply()
    {
        if (!IsLoaded) return;
        StopAll(); // stopping returns every animated property to its resting pose
        if (!Active) return;
        var sb = (Storyboard)Resources[State.ToString()];
        Timeline.SetDesiredFrameRate(sb, FrameRate);
        sb.Begin(this, true);
    }

    static Brush Frozen(string hex)
    {
        var b = (Brush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }
}

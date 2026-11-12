using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Modules.Agents;

namespace DynamicIsland.Views;

/// <summary>
/// Plays the ChatGPT app's own pet: typing at a laptop while working, breathing/blinking (with an occasional wave) when idle, eyes shut
/// when the app is closed. Frames only advance while <see cref="Active"/> (island expanded and this view showing).
/// </summary>
public partial class SpritePetControl : UserControl
{
    public static readonly DependencyProperty SpritesProperty = DependencyProperty.Register(nameof(Sprites), typeof(SpriteSet), typeof(SpritePetControl),
        new PropertyMetadata(null, (d, _) => ((SpritePetControl)d).Apply()));
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(PetState), typeof(SpritePetControl),
        new PropertyMetadata(PetState.Idle, (d, _) => ((SpritePetControl)d).Apply()));
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.Register(nameof(Active), typeof(bool), typeof(SpritePetControl),
        new PropertyMetadata(false, (d, _) => ((SpritePetControl)d).Apply()));

    public SpriteSet? Sprites { get => (SpriteSet?)GetValue(SpritesProperty); set => SetValue(SpritesProperty, value); }
    public PetState State { get => (PetState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public bool Active { get => (bool)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }

    readonly DispatcherTimer _timer = new();
    IReadOnlyList<BitmapSource> _sequence = [];
    int _frame, _idleLoops;
    bool _waving;

    public SpritePetControl()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Step();
        Unloaded += (_, _) => _timer.Stop();
    }

    void Apply()
    {
        _timer.Stop();
        if (Sprites is not { } s) { Frame.Source = null; return; }

        _frame = 0; _idleLoops = 0; _waving = false;
        if (State == PetState.Sleeping) { Frame.Source = s.Asleep; return; }

        _sequence = State == PetState.Working ? s.Working : s.Idle;
        Frame.Source = _sequence[0];
        if (!Active) return;

        _timer.Interval = TimeSpan.FromMilliseconds(State == PetState.Working ? 150 : 240);
        _timer.Start();
    }

    void Step()
    {
        if (Sprites is not { } s) return;
        _frame++;
        if (_frame >= _sequence.Count)
        {
            _frame = 0;
            if (State == PetState.Idle)
            {
                // idle: a few breathing loops, then a wave
                if (_waving) { _waving = false; _sequence = s.Idle; }
                else if (++_idleLoops >= 4) { _idleLoops = 0; _waving = true; _sequence = s.Wave; }
            }
        }
        Frame.Source = _sequence[_frame];
    }
}

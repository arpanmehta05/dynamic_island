namespace DynamicIsland.Modules.Agents;

/// <summary>The first footer button: the music/video player (the media module's view). Pulses while something is playing.</summary>
sealed class PlayerEntry : TabEntry
{
    readonly MediaModule _media;

    public override ViewKind Kind => ViewKind.Player;
    public override string Name => "Player";
    public override string Glyph => ""; // Segoe Fluent Icons: play
    public override string GlyphFont => "Segoe Fluent Icons, Segoe MDL2 Assets";
    public override string Accent => "#FF6B8A";
    /// <summary>Hidden while nothing is playing or paused anywhere (the island then simply shows the clock as its home screen).</summary>
    public override bool IsAvailable => _media.HasMedia;

    public PlayerEntry(MediaModule media)
    {
        _media = media;
        media.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MediaModule.IsPlaying) or nameof(MediaModule.HasMedia)) Sync();
        };
        Sync();
    }

    void Sync()
    {
        IsWorking = _media.IsPlaying;
        IsRunning = true;
        Raise(nameof(IsAvailable));
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using UltrastarDJ.App.Game;
using UltrastarDJ.Core.Game;
using UltrastarDJ.Core.Songs;
using UltrastarDJ.Core.Timing;

namespace UltrastarDJ.App.Controls;

/// <summary>
/// Draws the game on top of the video: one note lane per player (target bars, sung fill, playhead),
/// the lyrics bar with syllable sweep and lead-in, a progress line and PERFECT flashes.
/// Geometry per phrase is cached; per frame only fills, playhead and sweep are computed.
/// </summary>
public sealed class GameOverlayControl : Control
{
    public static readonly StyledProperty<GameScene?> SceneProperty = AvaloniaProperty.Register<GameOverlayControl, GameScene?>(nameof(Scene));
    public static readonly StyledProperty<bool> IsRunningProperty = AvaloniaProperty.Register<GameOverlayControl, bool>(nameof(IsRunning));
    public static readonly StyledProperty<bool> ShowPianoRollLinesProperty = AvaloniaProperty.Register<GameOverlayControl, bool>(nameof(ShowPianoRollLines), true);

    private static readonly FontFamily Font = FontFamily.Parse("fonts:Inter#Inter");
    private static readonly Typeface Regular = new(Font, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface Bold = new(Font, FontStyle.Normal, FontWeight.Bold);
    private static readonly IBrush Golden = new SolidColorBrush(Color.Parse("#FFD700"));
    private static readonly IBrush BarFill = new SolidColorBrush(Color.FromArgb(0xB4, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush BarStroke = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush LaneBackdrop = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0));
    private static readonly IBrush LyricsBackdrop = new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0));
    private static readonly IBrush LyricsText = Brushes.White;
    private static readonly IBrush LyricsNext = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush LeadIn = new SolidColorBrush(Color.Parse("#4F8EF7"));
    private static readonly IBrush PianoLine = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
    private static readonly Pen PlayheadPen = new(Brushes.White, 2);
    private static readonly Pen RapPen = new(BarStroke, 2) { DashStyle = DashStyle.Dash };
    private static readonly Pen FreestylePen = new(BarStroke, 2) { DashStyle = DashStyle.Dot };
    private static readonly Pen GoldenPen = new(Golden, 2);

    private const double LeadInSec = 3.0;
    private const double PerfectFlashSec = 1.2;

    private readonly DispatcherTimer _frame;
    private readonly Dictionary<int, PhraseCache> _phraseCache = [];

    static GameOverlayControl()
    {
        AffectsRender<GameOverlayControl>(SceneProperty);
        IsRunningProperty.Changed.AddClassHandler<GameOverlayControl>((c, _) => c.UpdateTimer());
        SceneProperty.Changed.AddClassHandler<GameOverlayControl>((c, _) => { c._phraseCache.Clear(); c.UpdateTimer(); });
    }

    public GameOverlayControl()
    {
        _frame = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => InvalidateVisual());
    }

    public GameScene? Scene { get => GetValue(SceneProperty); set => SetValue(SceneProperty, value); }
    public bool IsRunning { get => GetValue(IsRunningProperty); set => SetValue(IsRunningProperty, value); }
    public bool ShowPianoRollLines { get => GetValue(ShowPianoRollLinesProperty); set => SetValue(ShowPianoRollLinesProperty, value); }

    private void UpdateTimer()
    {
        if (IsRunning && Scene is not null && IsAttachedToVisualTree())
        {
            _frame.Start();
        }
        else
        {
            _frame.Stop();
            InvalidateVisual();
        }
    }

    private bool IsAttachedToVisualTree() => _attached;

    private bool _attached;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        _frame.Stop();
    }

    // ── Layout ──────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        DrawingContext ctx = context;
        GameScene? scene = Scene;
        if (scene is null || scene.Players.Count == 0)
        {
            return;
        }

        Rect bounds = new(Bounds.Size);
        double pos = scene.PositionSec();
        double beat = scene.Session.BeatAt(pos);
        double lyricsH = Math.Clamp(bounds.Height * 0.16, 90, 170);
        double progressH = 4;
        Rect lanesArea = new(0, progressH, bounds.Width, bounds.Height - lyricsH - progressH);
        Rect lyricsArea = new(0, bounds.Height - lyricsH, bounds.Width, lyricsH);

        lock (scene.Sync)
        {
            DrawProgress(ctx, new Rect(0, 0, bounds.Width, progressH), scene, pos);

            int n = scene.Players.Count;
            double laneH = lanesArea.Height / n;
            for (int i = 0; i < n; i++)
            {
                ScenePlayer p = scene.Players[i];
                Rect lane = new(lanesArea.X, lanesArea.Y + i * laneH, lanesArea.Width, laneH);
                DrawLane(ctx, lane.Deflate(new Thickness(24, 12)), scene, p, beat, pos, n);
            }

            // Lyrics follow the first player's track (duets share a lyric line per track; keep it simple for now).
            DrawLyrics(ctx, lyricsArea, scene, scene.Players[0], beat, pos);
        }
    }

    private static void DrawProgress(DrawingContext ctx, Rect r, GameScene scene, double pos)
    {
        double endSec = BeatMath.SecondsAt(scene.Session.LastBeat, scene.Session.Song.Bpm, scene.Session.Song.GapMs);
        double frac = endSec > 0 ? Math.Clamp(pos / endSec, 0, 1) : 0;
        ctx.FillRectangle(LaneBackdrop, r);
        ctx.FillRectangle(Brushes.White, new Rect(r.X, r.Y, r.Width * frac, r.Height));
    }

    // ── Note lane ───────────────────────────────────────────────────────

    private void DrawLane(DrawingContext ctx, Rect lane, GameScene scene, ScenePlayer player, double beat, double pos, int playersOnScreen)
    {
        LaneState state = scene.Lanes[player.Id];
        double extend = BeatMath.MsToBeats(scene.Session.Song.Bpm, 250); // keep the phrase while late mic data arrives
        LyricLine? line = NoteLaneGeometry.ActiveLine(state.Track, beat, extend);
        if (line is null)
        {
            return;
        }

        int rows = NoteLaneGeometry.RowCount(playersOnScreen);
        PhraseCache cache = GetPhraseCache(player.Id, line, rows);
        double rowH = lane.Height / rows;
        double barH = Math.Max(10, Math.Min(rowH * 0.9, playersOnScreen <= 2 ? 40 : 28));
        double radius = playersOnScreen <= 2 ? 8 : 4;

        // Player label + score
        PlayerScorer scorer = scene.Session.Scorer(player.Id);
        FormattedText label = new($"{player.Name}   {scorer.Score}", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, Math.Max(14, lane.Height * 0.06), player.Brush);
        ctx.DrawText(label, new Point(lane.X, lane.Y - 4));

        if (ShowPianoRollLines)
        {
            for (int r = 0; r <= rows; r += 2)
            {
                double y = lane.Y + r * rowH;
                ctx.FillRectangle(PianoLine, new Rect(lane.X, y, lane.Width, 1));
            }
        }

        // Target bars
        foreach (NoteBox box in cache.Boxes)
        {
            Rect rect = BoxRect(box, lane, rowH, barH);
            RoundedRect rr = new(rect, radius);
            switch (box.Note.Type)
            {
                case NoteType.Golden:
                case NoteType.RapGolden:
                    ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xD7, 0x00)), GoldenPen, rr);
                    break;
                case NoteType.Rap:
                    ctx.DrawRectangle(null, RapPen, rr);
                    break;
                case NoteType.Freestyle:
                    ctx.DrawRectangle(null, FreestylePen, rr);
                    break;
                default:
                    ctx.DrawRectangle(BarFill, null, rr);
                    break;
            }
        }

        // Sung fill: consecutive beats with the same verdict/row form one segment.
        IBrush correctBrush = player.Brush;
        IBrush wrongBrush = new SolidColorBrush(player.Color, 0.45);
        foreach (NoteBox box in cache.Boxes)
        {
            Note note = box.Note;
            int segStart = -1;
            BeatResult segResult = default;
            for (int b = note.StartBeat; b <= note.EndBeat; b++)
            {
                bool has = b < note.EndBeat && state.Results.TryGetValue(b, out BeatResult r);
                BeatResult cur = has ? state.Results[b] : default;
                bool sameSeg = has && segStart >= 0 && cur.Correct == segResult.Correct && (cur.Correct || Math.Abs(cur.RowPitch - segResult.RowPitch) < 0.5);
                if (!sameSeg)
                {
                    if (segStart >= 0)
                    {
                        DrawSegment(ctx, lane, cache, rows, rowH, barH, radius, box, segStart, b, segResult, correctBrush, wrongBrush);
                    }

                    segStart = has ? b : -1;
                    segResult = cur;
                }
            }

            // Fully-correct glow
            if (note.IsScorable && scorer.CorrectBeats(note) * 2 >= note.LengthBeats && note.LengthBeats > 0)
            {
                Rect rect = BoxRect(box, lane, rowH, barH).Inflate(3);
                ctx.DrawRectangle(null, new Pen(new SolidColorBrush(player.Color, 0.9), 3), new RoundedRect(rect, radius + 3));
            }
        }

        // Syllables inside bars (≥ 2 beats wide)
        foreach (NoteBox box in cache.Boxes)
        {
            if (box.Label is null)
            {
                continue;
            }

            Rect rect = BoxRect(box, lane, rowH, barH);
            if (rect.Width > box.Label.Width + 6)
            {
                ctx.DrawText(box.Label, new Point(rect.X + (rect.Width - box.Label.Width) / 2, rect.Y + (rect.Height - box.Label.Height) / 2));
            }
        }

        // Playhead
        double phraseStartSec = BeatMath.SecondsAt(line.FirstNoteBeat, scene.Session.Song.Bpm, scene.Session.Song.GapMs);
        double phraseEndSec = BeatMath.SecondsAt(line.EndBeat, scene.Session.Song.Bpm, scene.Session.Song.GapMs);
        double frac = (pos - phraseStartSec) / Math.Max(0.001, phraseEndSec - phraseStartSec);
        if (frac is >= 0 and <= 1)
        {
            double x = lane.X + lane.Width * frac;
            ctx.DrawLine(PlayheadPen, new Point(x, lane.Y), new Point(x, lane.Bottom));
        }

        // PERFECT flash
        if (scene.PerfectFlashAt.TryGetValue(player.Id, out double at) && pos - at < PerfectFlashSec)
        {
            double t = (pos - at) / PerfectFlashSec;
            FormattedText perfect = new("PERFECT", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, Math.Max(28, lane.Height * 0.18), new SolidColorBrush(Colors.Gold, 1 - t));
            ctx.DrawText(perfect, new Point(lane.Center.X - perfect.Width / 2, lane.Y + lane.Height * 0.3 - t * 30));
        }
    }

    private static void DrawSegment(DrawingContext ctx, Rect lane, PhraseCache cache, int rows, double rowH, double barH, double radius,
        NoteBox box, int fromBeat, int toBeat, BeatResult result, IBrush correct, IBrush wrong)
    {
        double x0 = lane.X + lane.Width * (fromBeat - cache.PhraseStart) / cache.PhraseBeats;
        double x1 = lane.X + lane.Width * (toBeat - cache.PhraseStart) / cache.PhraseBeats;
        int row = result.Correct ? box.Row : NoteLaneGeometry.PitchToRow((int)Math.Round(result.RowPitch), cache.AvgPitch, rows);
        double y = lane.Y + row * rowH + (rowH - barH) / 2;
        Rect r = new(x0, y, Math.Max(2, x1 - x0), barH);
        ctx.DrawRectangle(result.Correct ? correct : wrong, null, new RoundedRect(r, radius));
    }

    private static Rect BoxRect(NoteBox box, Rect lane, double rowH, double barH)
        => new(lane.X + lane.Width * box.X, lane.Y + box.Row * rowH + (rowH - barH) / 2, Math.Max(3, lane.Width * box.Width), barH);

    private PhraseCache GetPhraseCache(int playerId, LyricLine line, int rows)
    {
        if (_phraseCache.TryGetValue(playerId, out PhraseCache? c) && ReferenceEquals(c.Line, line) && c.Rows == rows)
        {
            return c;
        }

        double avg = NoteLaneGeometry.AveragePitch(line);
        double phraseStart = line.FirstNoteBeat;
        double phraseBeats = Math.Max(1, line.EndBeat - phraseStart);
        List<NoteBox> boxes = [];
        foreach (Note n in line.Notes)
        {
            (double x, double w) = NoteLaneGeometry.NoteSpan(n, line);
            FormattedText? label = n.LengthBeats >= 2 && n.Syllable.Trim().Length > 0
                ? new FormattedText(n.Syllable.Trim(), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Regular, 16, Brushes.Black)
                : null;
            boxes.Add(new NoteBox(n, x, w, NoteLaneGeometry.PitchToRow(n.UsPitch, avg, rows), label));
        }

        PhraseCache cache = new(line, rows, avg, phraseStart, phraseBeats, boxes);
        _phraseCache[playerId] = cache;
        return cache;
    }

    // ── Lyrics ──────────────────────────────────────────────────────────

    private static void DrawLyrics(DrawingContext ctx, Rect area, GameScene scene, ScenePlayer player, double beat, double pos)
    {
        ctx.FillRectangle(LyricsBackdrop, area);
        NoteTrack track = scene.Lanes[player.Id].Track;
        LyricLine? line = NoteLaneGeometry.ActiveLine(track, beat);
        if (line is null)
        {
            return;
        }

        int idx = track.Lines.ToList().IndexOf(line);
        LyricLine? next = idx >= 0 && idx + 1 < track.Lines.Count ? track.Lines[idx + 1] : null;

        double mainSize = Math.Clamp(area.Height * 0.36, 22, 46);
        double nextSize = mainSize * 0.55;
        double y = area.Y + area.Height * 0.18;

        // Current phrase: measure all syllables, centre the whole line, sweep each syllable by beat progress.
        List<FormattedText> parts = [];
        double total = 0;
        foreach (Note n in line.Notes)
        {
            FormattedText ft = new(n.Syllable, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, mainSize, LyricsText);
            parts.Add(ft);
            total += ft.WidthIncludingTrailingWhitespace;
        }

        double x = area.X + (area.Width - total) / 2;
        for (int i = 0; i < parts.Count; i++)
        {
            Note n = line.Notes[i];
            FormattedText ft = parts[i];
            ctx.DrawText(ft, new Point(x, y));
            double progress = Math.Clamp((beat - n.StartBeat) / Math.Max(1, n.LengthBeats), 0, 1);
            if (progress > 0)
            {
                using (ctx.PushClip(new Rect(x, y, ft.WidthIncludingTrailingWhitespace * progress, ft.Height)))
                {
                    ft.SetForegroundBrush(player.Brush);
                    ctx.DrawText(ft, new Point(x, y));
                    ft.SetForegroundBrush(LyricsText);
                }
            }

            x += ft.WidthIncludingTrailingWhitespace;
        }

        // Lead-in bar before a phrase that follows a gap: shrinks right→left over 3 s.
        double phraseStartSec = BeatMath.SecondsAt(line.FirstNoteBeat, scene.Session.Song.Bpm, scene.Session.Song.GapMs);
        double until = phraseStartSec - pos;
        if (until is > 0 and <= LeadInSec)
        {
            double w = area.Width * 0.3 * (until / LeadInSec);
            ctx.FillRectangle(LeadIn, new Rect(area.X + (area.Width - w) / 2, y - 10, w, 6));
        }

        if (next is not null)
        {
            string text = string.Concat(next.Notes.Select(n => n.Syllable));
            FormattedText ft = new(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Regular, nextSize, LyricsNext);
            ctx.DrawText(ft, new Point(area.X + (area.Width - ft.Width) / 2, y + mainSize * 1.35));
        }
    }

    private sealed record NoteBox(Note Note, double X, double Width, int Row, FormattedText? Label);
    private sealed record PhraseCache(LyricLine Line, int Rows, double AvgPitch, double PhraseStart, double PhraseBeats, IReadOnlyList<NoteBox> Boxes);
}

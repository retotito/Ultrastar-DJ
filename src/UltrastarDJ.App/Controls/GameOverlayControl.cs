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
/// Draws the game on top of the video: one note lane per player (target bars, sung fill, playhead) — or none when
/// no player is assigned to this beamer (lyrics only, karaoke without scoring) —
/// the lyrics bar with syllable sweep and lead-in, a progress line and PERFECT flashes.
/// Geometry per phrase is cached; per frame only fills, playhead and sweep are computed.
/// </summary>
public sealed class GameOverlayControl : Control
{
    public static readonly StyledProperty<GameScene?> SceneProperty = AvaloniaProperty.Register<GameOverlayControl, GameScene?>(nameof(Scene));
    public static readonly StyledProperty<bool> IsRunningProperty = AvaloniaProperty.Register<GameOverlayControl, bool>(nameof(IsRunning));
    public static readonly StyledProperty<bool> ShowPianoRollLinesProperty = AvaloniaProperty.Register<GameOverlayControl, bool>(nameof(ShowPianoRollLines), true);
    public static readonly StyledProperty<NoteBarStyle> NoteBarStyleProperty = AvaloniaProperty.Register<GameOverlayControl, NoteBarStyle>(nameof(NoteBarStyle));

    private static readonly FontFamily Font = FontFamily.Parse("fonts:Inter#Inter");
    private static readonly Typeface Regular = new(Font, FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface Bold = new(Font, FontStyle.Normal, FontWeight.Bold);
    private static readonly IBrush LyricsBackdrop = new SolidColorBrush(Color.FromArgb(0x8C, 0, 0, 0));
    private static readonly IBrush LyricsText = Brushes.White;
    private static readonly IBrush LyricsNext = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush LeadIn = new SolidColorBrush(Color.Parse("#4F8EF7"));
    private static readonly IBrush PianoLine = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush TimePill = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));
    private static readonly IBrush ProgressTrack = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

    private const double LeadInSec = 3.0;
    private const double ProgressBarH = 6;
    // Gap between the bottom of the lyrics / time pills and the progress bar.
    private const double AboveBar = 8;
    // Room above the current line for the lead-in bar (drawn 10 px above it, 6 px high) plus a little air.
    private const double LeadInRoom = 22;

    // ── Note bar look (prototype NoteLane.svelte) ──
    private sealed record BarLook(IBrush Background, Color Border);
    private static readonly BarLook WhiteBars = new(new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)), Color.FromArgb(89, 255, 255, 255));
    private static readonly BarLook BlackBars = new(new SolidColorBrush(Color.FromArgb(115, 0, 0, 0)), Color.FromArgb(140, 255, 255, 255));
    private static readonly Color Gold = Color.FromRgb(255, 210, 60);
    private static readonly IBrush GoldBarBg = new SolidColorBrush(Gold, 0.2);
    private static readonly Pen GoldBarPen = new(new SolidColorBrush(Gold, 0.85), 2);
    private static readonly IBrush GoldFill = new SolidColorBrush(Color.FromRgb(255, 215, 0), 0.85);
    private static readonly Color Orange = Color.FromRgb(255, 165, 50);
    private static readonly IBrush RapBarBg = new SolidColorBrush(Orange, 0.08);
    private static readonly Pen RapDashPen = new(new SolidColorBrush(Orange, 0.5), 2) { DashStyle = DashStyle.Dash };
    // Not DashStyle.Dot: that is {0, 2} — zero-length dashes, invisible with the default flat caps.
    private static readonly Pen FreestyleDotPen = new(new SolidColorBrush(Colors.White, 0.6), 1) { DashStyle = new DashStyle([2, 2], 0) };
    private static readonly IBrush BadgeBg = new SolidColorBrush(Colors.Black, 0.6);
    private static readonly IBrush RapBadge = new SolidColorBrush(Color.FromRgb(255, 140, 0));
    private static readonly IBrush FreestyleBadge = new SolidColorBrush(Colors.White, 0.55);
    private static readonly IBrush SyllableShadow = new SolidColorBrush(Colors.Black, 0.6);
    // Correct pulse: 0.5 s, peak at 40 % (prototype keyframes note-correct-pulse / -golden, rap-border-glow).
    private const double PulseSec = 0.5;

    // One redraw per display refresh while the game runs (TopLevel.RequestAnimationFrame), not a free-running timer:
    // frames land evenly on vsync, so the gliding fill and lyrics sweep move without judder.
    private bool _animating;
    private readonly Dictionary<int, PhraseCache> _phraseCache = [];

    static GameOverlayControl()
    {
        AffectsRender<GameOverlayControl>(SceneProperty);
        IsRunningProperty.Changed.AddClassHandler<GameOverlayControl>((c, _) => c.UpdateTimer());
        SceneProperty.Changed.AddClassHandler<GameOverlayControl>((c, _) => { c._phraseCache.Clear(); c.UpdateTimer(); });
    }

    public GameOverlayControl()
    {
    }

    public GameScene? Scene { get => GetValue(SceneProperty); set => SetValue(SceneProperty, value); }
    public bool IsRunning { get => GetValue(IsRunningProperty); set => SetValue(IsRunningProperty, value); }
    public bool ShowPianoRollLines { get => GetValue(ShowPianoRollLinesProperty); set => SetValue(ShowPianoRollLinesProperty, value); }
    public NoteBarStyle NoteBarStyle { get => GetValue(NoteBarStyleProperty); set => SetValue(NoteBarStyleProperty, value); }

    private void RequestFrame()
        => TopLevel.GetTopLevel(this)?.RequestAnimationFrame(_ =>
        {
            if (_animating)
            {
                InvalidateVisual();
                RequestFrame();
            }
        });

    private void UpdateTimer()
    {
        if (IsRunning && Scene is not null && IsAttachedToVisualTree())
        {
            if (!_animating)
            {
                _animating = true;
                RequestFrame();
            }
        }
        else
        {
            _animating = false;
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
        _animating = false;
    }

    // ── Layout ──────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        DrawingContext ctx = context;
        GameScene? scene = Scene;
        if (scene is null)
        {
            return;
        }

        Rect bounds = new(Bounds.Size);
        double pos = scene.PositionSec();
        double beat = scene.Session.BeatAt(pos);
        // Bottom strip: progress bar flush with the bottom edge; above it one backdrop holding the lyrics (anchored
        // to the bar) and the elapsed / remaining pills at its sides.
        // Font sizes follow the screen; the lyrics strip is exactly as high as its content (lead-in bar, current line,
        // next line, gap to the progress bar) so no empty band sits above the words.
        double mainSize = Math.Clamp(bounds.Height * 0.058, 22, 46);
        double nextSize = mainSize * 0.55;
        double lyricsH = LeadInRoom + mainSize * 1.35 + nextSize * 1.3 + AboveBar;
        double timeSize = Math.Clamp(bounds.Height * 0.022, 13, 22);
        Rect bar = new(0, bounds.Height - ProgressBarH, bounds.Width, ProgressBarH);
        Rect lyricsArea = new(0, bar.Y - lyricsH, bounds.Width, lyricsH);
        Rect lanesArea = new(0, 0, bounds.Width, lyricsArea.Y);

        lock (scene.Sync)
        {

            int n = scene.Players.Count;
            if (n > 0)
            {
                double laneH = lanesArea.Height / n;
                for (int i = 0; i < n; i++)
                {
                    ScenePlayer p = scene.Players[i];
                    Rect lane = new(lanesArea.X, lanesArea.Y + i * laneH, lanesArea.Width, laneH);
                    DrawLane(ctx, lane.Deflate(new Thickness(24, 12)), scene, p, beat, pos, n);
                }
            }

            // Lyrics follow the first player's track (duets share a lyric line per track; keep it simple for now).
            // No player on this beamer: karaoke without scoring — lyrics of the song's first track, no lanes.
            NoteTrack lyricsTrack = n > 0 ? scene.Lanes[scene.Players[0].Id].Track : scene.Session.Song.Notes![0];
            IBrush sweep = n > 0 ? scene.Players[0].Brush : LeadIn;
            ctx.FillRectangle(LyricsBackdrop, new Rect(0, lyricsArea.Y, bounds.Width, lyricsH + ProgressBarH));
            DrawLyrics(ctx, lyricsArea, lyricsTrack, scene.Session.Song, sweep, beat, pos, mainSize, nextSize);
            DrawTimes(ctx, lyricsArea, bar, scene, sweep, timeSize);
        }
    }

    /// <summary>
    /// Prototype's SongProgress: elapsed (left) and remaining (right) in pills, the bar under them. Times come from
    /// <see cref="SongTimeline"/> (#START, #END, #GAP, #VIDEOGAP and the media length are already in it).
    /// </summary>
    private void DrawTimes(DrawingContext ctx, Rect lyricsArea, Rect bar, GameScene scene, IBrush fill, double size)
    {
        ctx.FillRectangle(ProgressTrack, bar);
        if (scene.Timeline is not { } timeline)
        {
            return;
        }

        double t = scene.ClockSec();
        ctx.FillRectangle(fill, bar.WithWidth(bar.Width * timeline.Fraction(t)));

        const double side = 24;
        double y = bar.Y - AboveBar - size * 1.6;
        DrawPill(ctx, Pill(ref _elapsedText, timeline.Elapsed(t), size), size, new Point(lyricsArea.X + side, y), alignRight: false);
        DrawPill(ctx, Pill(ref _remainingText, timeline.Remaining(t), size), size, new Point(lyricsArea.Right - side, y), alignRight: true);
    }

    // Time pill texts change once a second: rebuilt only then.
    private (int Sec, double Size, FormattedText Text)? _elapsedText;
    private (int Sec, double Size, FormattedText Text)? _remainingText;

    private static FormattedText Pill(ref (int Sec, double Size, FormattedText Text)? cache, double seconds, double size)
    {
        int sec = (int)Math.Floor(seconds);
        if (cache is not { } c || c.Sec != sec || c.Size != size)
        {
            cache = c = (sec, size, new FormattedText(Clock(sec), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Regular, size, LyricsText));
        }

        return c.Text;
    }

    private static void DrawPill(DrawingContext ctx, FormattedText ft, double size, Point at, bool alignRight)
    {
        double w = ft.Width + size * 1.2;
        double h = size * 1.6;
        Rect pill = new(alignRight ? at.X - w : at.X, at.Y, w, h);
        ctx.DrawRectangle(TimePill, null, new RoundedRect(pill, h / 2));
        ctx.DrawText(ft, new Point(pill.X + (w - ft.Width) / 2, pill.Y + (h - ft.Height) / 2));
    }

    private static string Clock(int s) => $"{s / 60}:{s % 60:00}";

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
        // Prototype sizes: ≤ 2 players per beamer 16 rows, bars ≥ 40 px, radius 8; 3–4 players 12 rows, ≥ 28 px, radius 4.
        double barH = Math.Max(rowH * 0.8, playersOnScreen <= 2 ? 40 : 28);
        double radius = playersOnScreen <= 2 ? 8 : 4;
        BarLook look = NoteBarStyle == NoteBarStyle.Black ? BlackBars : WhiteBars;
        PlayerPaint paint = Paint(player.Color);
        // What this player's mic is singing now (song beat − mic delay): the sung fill grows up to it, like the scorer.
        double sungBeat = scene.Session.SungBeatAt(pos, player.Id);

        // Player label + score
        PlayerScorer scorer = scene.Session.Scorer(player.Id);
        double labelSize = Math.Max(14, lane.Height * 0.06);
        if (!_labels.TryGetValue(player.Id, out (int Score, double Size, FormattedText Text) lab) || lab.Score != scorer.Score || lab.Size != labelSize)
        {
            lab = (scorer.Score, labelSize, new FormattedText($"{player.Name}   {scorer.Score}", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, labelSize, player.Brush));
            _labels[player.Id] = lab;
        }

        FormattedText label = lab.Text;
        ctx.DrawText(label, new Point(lane.X, lane.Y - 4));

        if (ShowPianoRollLines)
        {
            for (int r = 0; r <= rows; r += 2)
            {
                double y = lane.Y + r * rowH;
                ctx.FillRectangle(PianoLine, new Rect(lane.X, y, lane.Width, 1));
            }
        }

        // Bars with their correct fill inside, syllable and badge.
        foreach (NoteBox box in cache.Boxes)
        {
            Rect rect = BoxRect(box, lane, rowH, barH);
            RoundedRect rr = new(rect, radius);
            DrawBar(ctx, rr, box.Note, paint, state, scorer, beat, pos, look);
            if (!HasFill(box.Note))
            {
                DrawSyllable(ctx, box, rect);
                DrawBadge(ctx, box.Note, rect, state);
                DrawNoteStars(ctx, box.Note, rect, scorer, sungBeat, pos);
                continue;
            }

            using (ctx.PushClip(rr))
            {
                IBrush fill = box.Note.Type == NoteType.Golden ? GoldFill : paint.Fill;
                foreach ((double from, double to, BeatResult result) in SungSegments(box.Note, state, sungBeat))
                {
                    if (result.Correct)
                    {
                        ctx.FillRectangle(fill, new Rect(BeatX(lane, cache, from), rect.Y, Math.Max(2, BeatX(lane, cache, to) - BeatX(lane, cache, from)), rect.Height));
                    }
                }
            }

            if (box.Note.Type == NoteType.Golden)
            {
                foreach ((double from, double to, BeatResult result) in SungSegments(box.Note, state, sungBeat))
                {
                    if (result.Correct)
                    {
                        // The segment ending at the sung beat is being hit right now: its edge twinkles.
                        bool hitting = sungBeat < box.Note.EndBeat && Math.Abs(to - sungBeat) < 0.001;
                        double x1 = BeatX(lane, cache, to);
                        ScoreEffects.DrawGoldenSparkles(ctx, rect, BeatX(lane, cache, from), x1, pos, box.Note.StartBeat + (int)from, hitting ? x1 : null);
                    }
                }
            }

            DrawSyllable(ctx, box, rect);
            DrawBadge(ctx, box.Note, rect, state);
            DrawNoteStars(ctx, box.Note, rect, scorer, sungBeat, pos);
        }

        // Wrong pitch: the player's colour at half strength, on the row actually sung (above the bars).
        IBrush wrong = paint.Wrong;
        foreach (NoteBox box in cache.Boxes)
        {
            if (!HasFill(box.Note))
            {
                continue;
            }

            foreach ((double from, double to, BeatResult result) in SungSegments(box.Note, state, sungBeat))
            {
                if (!result.Correct)
                {
                    int row = NoteLaneGeometry.PitchToRow((int)Math.Round(result.RowPitch), cache.AvgPitch, rows);
                    double x0 = BeatX(lane, cache, from);
                    Rect r = new(x0, lane.Y + row * rowH + (rowH - barH) / 2, Math.Max(2, BeatX(lane, cache, to) - x0), barH);
                    ctx.DrawRectangle(wrong, null, new RoundedRect(r, radius));
                }
            }
        }

        // End of a phrase: the 100 % star burst, then the rating word above everything (USDX, see ScoreEffects).
        if (scene.Ratings.TryGetValue(player.Id, out PhraseResult rated))
        {
            ScoreEffects.DrawBurst(ctx, lane, rated, pos, paint.StarHalo);
            _effects.DrawRating(ctx, lane, rated, pos, paint.TextGlow);
        }
    }

    /// <summary>Any voice recorded on the note (freestyle ticks carry their beat when the mic is not silent).</summary>
    private static bool Heard(Note note, LaneState state)
    {
        for (int b = note.StartBeat; b < note.EndBeat; b++)
        {
            if (state.Results.ContainsKey(b))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Rap shows a hit on its border only (any voice counts, so a fill would just paint the whole bar);
    /// freestyle is not scored. Prototype NoteLane: neither gets a sung fill.
    /// </summary>
    private static bool HasFill(Note note) => !note.IsRap && note.Type != NoteType.Freestyle;

    /// <summary>USDX perfect note: every beat right, and the mic is past its end (not while it is still sung).</summary>
    private static void DrawNoteStars(DrawingContext ctx, Note note, Rect rect, PlayerScorer scorer, double sungBeat, double pos)
    {
        if (sungBeat >= note.EndBeat && scorer.IsNotePerfect(note))
        {
            ScoreEffects.DrawNoteStars(ctx, rect, pos, note.StartBeat, hasBadge: note.IsRap);
        }
    }

    private static double BeatX(Rect lane, PhraseCache cache, double beat) => lane.X + lane.Width * (beat - cache.PhraseStart) / cache.PhraseBeats;

    // Reused per call (each caller iterates the result before the next call): no list per note per frame.
    private static readonly List<(double From, double To, BeatResult Result)> SegmentBuffer = [];

    /// <summary>
    /// Sung segments of one note: consecutive scored beats with the same verdict (and, when wrong, the same row).
    /// The beat being sung right now ends at the continuous <paramref name="sungBeat"/> instead of its whole-beat
    /// end (USDX SingDrawPlayerLine: right − (1 − frac(MidBeatD))) — the edge glides instead of jumping per beat.
    /// </summary>
    private static List<(double From, double To, BeatResult Result)> SungSegments(Note note, LaneState state, double sungBeat)
    {
        List<(double From, double To, BeatResult Result)> list = SegmentBuffer;
        list.Clear();
        int segStart = -1;
        BeatResult seg = default;
        for (int b = note.StartBeat; b <= note.EndBeat; b++)
        {
            bool has = b < note.EndBeat && state.Results.TryGetValue(b, out _);
            BeatResult cur = has ? state.Results[b] : default;
            bool same = has && segStart >= 0 && cur.Correct == seg.Correct && (cur.Correct || Math.Abs(cur.RowPitch - seg.RowPitch) < 0.5);
            if (!same)
            {
                if (segStart >= 0)
                {
                    double end = Math.Floor(sungBeat) == b - 1 ? Math.Max(b - 1, sungBeat) : b;
                    list.Add((segStart, end, seg));
                }

                segStart = has ? b : -1;
                seg = cur;
            }
        }

        return list;
    }

    /// <summary>One target bar in the prototype's look, with its effects (timestamps kept in <see cref="LaneState"/>).</summary>
    private static void DrawBar(DrawingContext ctx, RoundedRect rr, Note note, PlayerPaint paint, LaneState state, PlayerScorer scorer,
        double beat, double pos, BarLook look)
    {
        bool correct = note.IsScorable && note.LengthBeats > 0 && scorer.CorrectBeats(note) * 2 >= note.LengthBeats;
        if (correct)
        {
            state.CorrectSince.TryAdd(note.StartBeat, pos);
        }

        switch (note.Type)
        {
            case NoteType.Golden:
                // Golden: gold tint and border, glowing, shimmering (opacity 0.75 ↔ 1 over 1.4 s).
                using (ctx.PushOpacity(0.875 + 0.125 * Math.Sin(2 * Math.PI * pos / 1.4)))
                {
                    ctx.DrawRectangle(GoldBarBg, GoldBarPen, rr, GoldGlow);
                }

                if (correct)
                {
                    DrawPulse(ctx, rr, Color.FromRgb(255, 215, 0), pos - state.CorrectSince[note.StartBeat], golden: true);
                }

                break;

            case NoteType.Rap or NoteType.RapGolden:
                // Rap: dashed orange; once hit, solid with an orange glow that settles over 0.5 s.
                if (scorer.CorrectBeats(note) > 0)
                {
                    state.RapHitAt.TryAdd(note.StartBeat, pos);
                }

                if (state.RapHitAt.TryGetValue(note.StartBeat, out double hit))
                {
                    double k = Math.Clamp((pos - hit) / PulseSec, 0, 1);
                    if (k >= 1)
                    {
                        ctx.DrawRectangle(RapBarBg, SettledRap, rr, SettledRapGlow);
                    }
                    else
                    {
                        ctx.DrawRectangle(RapBarBg, new Pen(new SolidColorBrush(Orange, Lerp(1, 0.8, k)), 2), rr, GlowOf(Color.FromRgb(255, 140, 0), Lerp(0.85, 0.35, k), Lerp(18, 6, k)));
                    }
                }
                else
                {
                    ctx.DrawRectangle(RapBarBg, RapDashPen, rr);
                }

                break;

            case NoteType.Freestyle:
                // Freestyle: thin white dots; once the player is heard on it, a solid border in their colour with a glow.
                // Not scored — the voice alone lights it, like a rap hit.
                if (Heard(note, state))
                {
                    ctx.DrawRectangle(null, paint.FreestyleLit, rr, paint.FreestyleGlow);
                }
                else
                {
                    ctx.DrawRectangle(null, FreestyleDotPen, rr);
                }

                break;

            default:
                // Normal: see-through (white or black style), border tinted with the player's colour, soft glow.
                ctx.DrawRectangle(look.Background, ReferenceEquals(look, BlackBars) ? paint.BorderOnBlack : paint.BorderOnWhite, rr, paint.Glow);
                if (correct)
                {
                    DrawPulse(ctx, rr, Colors.White, pos - state.CorrectSince[note.StartBeat], golden: false);
                }

                break;
        }
    }

    /// <summary>≥ 50 % correct: the border flares (peak at 40 % of 0.5 s) and settles to a lasting glow.</summary>
    private static void DrawPulse(DrawingContext ctx, RoundedRect rr, Color color, double sinceSec, bool golden)
    {
        double t = Math.Clamp(sinceSec / PulseSec, 0, 1);
        if (t >= 1)
        {
            ctx.DrawRectangle(null, golden ? SettledGold : SettledWhite, rr, golden ? SettledGoldGlow : SettledWhiteGlow);
            return;
        }

        (double border, double glow, double blur) = t < 0.4
            ? (Lerp(0.6, 1, t / 0.4), Lerp(0, golden ? 0.75 : 0.5, t / 0.4), Lerp(0, golden ? 14 : 10, t / 0.4))
            : (Lerp(1, golden ? 0.9 : 0.7, (t - 0.4) / 0.6), Lerp(golden ? 0.75 : 0.5, golden ? 0.4 : 0.25, (t - 0.4) / 0.6), Lerp(golden ? 14 : 10, golden ? 8 : 6, (t - 0.4) / 0.6));
        ctx.DrawRectangle(null, new Pen(new SolidColorBrush(color, border), 2), rr, GlowOf(color, glow, blur));
    }

    private static void DrawSyllable(DrawingContext ctx, NoteBox box, Rect rect)
    {
        if (box.Label is not { } text || rect.Width <= text.Width + 6)
        {
            return;
        }

        Point at = new(rect.X + (rect.Width - text.Width) / 2, rect.Y + (rect.Height - text.Height) / 2);
        text.SetForegroundBrush(SyllableShadow);
        ctx.DrawText(text, at + new Point(0, 1));
        text.SetForegroundBrush(Brushes.White);
        ctx.DrawText(text, at);
    }

    /// <summary>"R" (rap), "★" (golden rap), "F" (freestyle) above the bar's right end.</summary>
    private static readonly Dictionary<(string, IBrush), FormattedText> Badges = [];

    private static void DrawBadge(DrawingContext ctx, Note note, Rect rect, LaneState state)
    {
        (string? mark, IBrush? brush) = note.Type switch
        {
            NoteType.Rap => ("R", state.RapHitAt.ContainsKey(note.StartBeat) ? Brushes.White : RapBadge),
            NoteType.RapGolden => ("★", state.RapHitAt.ContainsKey(note.StartBeat) ? Brushes.White : RapBadge),
            NoteType.Freestyle => ("F", FreestyleBadge),
            _ => (null, null),
        };
        if (mark is null)
        {
            return;
        }

        if (!Badges.TryGetValue((mark, brush!), out FormattedText? ft))
        {
            ft = new FormattedText(mark, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, 12, brush!);
            Badges[(mark, brush!)] = ft;
        }

        Rect bg = new(rect.Right - 3 - ft.Width - 4, rect.Y - 2 - ft.Height - 2, ft.Width + 4, ft.Height + 2);
        ctx.DrawRectangle(BadgeBg, null, new RoundedRect(bg, 2));
        ctx.DrawText(ft, new Point(bg.X + 2, bg.Y + 1));
    }

    private static BoxShadows GlowOf(Color color, double alpha, double blur)
        => new(new BoxShadow { Blur = blur, Color = Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), color.R, color.G, color.B) });

    /// <summary>CSS color-mix: <paramref name="share"/> of <paramref name="a"/>, the rest <paramref name="b"/> (alpha too).</summary>
    private static Color Mix(Color a, Color b, double share)
        => Color.FromArgb(
            (byte)Lerp(b.A, a.A, share), (byte)Lerp(b.R, a.R, share), (byte)Lerp(b.G, a.G, share), (byte)Lerp(b.B, a.B, share));

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;

    /// <summary>Everything a lane draws in a player's colour, built once per colour (no per-frame brushes or pens).</summary>
    private sealed class PlayerPaint(Color c)
    {
        public IBrush Fill { get; } = new SolidColorBrush(c, 0.85);
        public IBrush Wrong { get; } = new SolidColorBrush(c, 0.5);
        public Pen BorderOnWhite { get; } = new(new SolidColorBrush(Mix(c, WhiteBars.Border, 0.55)), 2);
        public Pen BorderOnBlack { get; } = new(new SolidColorBrush(Mix(c, BlackBars.Border, 0.55)), 2);
        public BoxShadows Glow { get; } = GlowOf(c, 0.3, 7);
        public Pen FreestyleLit { get; } = new(new SolidColorBrush(Mix(c, Colors.White, 0.9)), 2);
        public BoxShadows FreestyleGlow { get; } = GlowOf(c, 0.5, 16);
        public IBrush TextGlow { get; } = new SolidColorBrush(c, 0.35);
        public IBrush StarHalo { get; } = new SolidColorBrush(c, 0.85);
    }

    private readonly Dictionary<Color, PlayerPaint> _paint = [];
    // "Name   score" per player, rebuilt when the score changes.
    private readonly Dictionary<int, (int Score, double Size, FormattedText Text)> _labels = [];
    private readonly ScoreEffects _effects = new();

    private PlayerPaint Paint(Color c) => _paint.TryGetValue(c, out PlayerPaint? p) ? p : _paint[c] = new PlayerPaint(c);

    // The settled state of the correct pulse and the rap glow (t ≥ 1) is constant: drawn with fixed objects.
    private static readonly Pen SettledWhite = new(new SolidColorBrush(Colors.White, 0.7), 2);
    private static readonly BoxShadows SettledWhiteGlow = GlowOf(Colors.White, 0.25, 6);
    private static readonly Pen SettledGold = new(new SolidColorBrush(Color.FromRgb(255, 215, 0), 0.9), 2);
    private static readonly BoxShadows SettledGoldGlow = GlowOf(Color.FromRgb(255, 215, 0), 0.4, 8);
    private static readonly Pen SettledRap = new(new SolidColorBrush(Orange, 0.8), 2);
    private static readonly BoxShadows SettledRapGlow = GlowOf(Color.FromRgb(255, 140, 0), 0.35, 6);
    private static readonly BoxShadows GoldGlow = GlowOf(Gold, 0.45, 8);

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

    // sweep: colour of the sung part of a syllable — the first player's, or the lead-in blue without players.
    // Texts of the current lyric line, built once per line and size — not per frame (each frame's garbage costs GC
    // pauses, and a GC pause stalls the audio callbacks on the same CoreAudio thread → crackling).
    private sealed record LyricsCache(LyricLine Line, double Size, FormattedText[] Parts, double Total, FormattedText? Next);
    private LyricsCache? _lyrics;

    private void DrawLyrics(DrawingContext ctx, Rect area, NoteTrack track, Song song, IBrush sweep, double beat, double pos,
        double mainSize, double nextSize)
    {
        LyricLine? line = NoteLaneGeometry.ActiveLine(track, beat);
        if (line is null)
        {
            return;
        }

        LyricsCache c = _lyrics is { } cached && ReferenceEquals(cached.Line, line) && cached.Size == mainSize
            ? cached
            : (_lyrics = BuildLyrics(track, line, mainSize, nextSize));

        // Anchored to the bottom: the next phrase ends just above the progress bar, the current line above it.
        double y = area.Bottom - AboveBar - nextSize * 1.3 - mainSize * 1.35;

        // Current phrase centred, each syllable swept by its beat progress.
        double x = area.X + (area.Width - c.Total) / 2;
        for (int i = 0; i < c.Parts.Length; i++)
        {
            Note n = line.Notes[i];
            FormattedText ft = c.Parts[i];
            ctx.DrawText(ft, new Point(x, y));
            double progress = Math.Clamp((beat - n.StartBeat) / Math.Max(1, n.LengthBeats), 0, 1);
            if (progress > 0)
            {
                using (ctx.PushClip(new Rect(x, y, ft.WidthIncludingTrailingWhitespace * progress, ft.Height)))
                {
                    ft.SetForegroundBrush(sweep);
                    ctx.DrawText(ft, new Point(x, y));
                    ft.SetForegroundBrush(LyricsText);
                }
            }

            x += ft.WidthIncludingTrailingWhitespace;
        }

        // Lead-in bar before a phrase that follows a gap: shrinks right→left over 3 s.
        double phraseStartSec = BeatMath.SecondsAt(line.FirstNoteBeat, song.Bpm, song.GapMs);
        double until = phraseStartSec - pos;
        if (until is > 0 and <= LeadInSec)
        {
            double w = area.Width * 0.3 * (until / LeadInSec);
            ctx.FillRectangle(LeadIn, new Rect(area.X + (area.Width - w) / 2, y - 10, w, 6));
        }

        if (c.Next is { } next)
        {
            ctx.DrawText(next, new Point(area.X + (area.Width - next.Width) / 2, y + mainSize * 1.35));
        }
    }

    private static LyricsCache BuildLyrics(NoteTrack track, LyricLine line, double mainSize, double nextSize)
    {
        FormattedText[] parts = new FormattedText[line.Notes.Count];
        double total = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = new FormattedText(line.Notes[i].Syllable, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Bold, mainSize, LyricsText);
            total += parts[i].WidthIncludingTrailingWhitespace;
        }

        LyricLine? nextLine = null;
        for (int i = 0; i < track.Lines.Count - 1; i++)
        {
            if (ReferenceEquals(track.Lines[i], line))
            {
                nextLine = track.Lines[i + 1];
                break;
            }
        }

        FormattedText? next = nextLine is null
            ? null
            : new FormattedText(string.Concat(nextLine.Notes.Select(n => n.Syllable)), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Regular, nextSize, LyricsNext);
        return new LyricsCache(line, mainSize, parts, total, next);
    }

    private sealed record NoteBox(Note Note, double X, double Width, int Row, FormattedText? Label);
    private sealed record PhraseCache(LyricLine Line, int Rows, double AvgPitch, double PhraseStart, double PhraseBeats, IReadOnlyList<NoteBox> Boxes);
}

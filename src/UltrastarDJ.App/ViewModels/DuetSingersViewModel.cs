using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltrastarDJ.App.Services;
using UltrastarDJ.Core.Displays;
using UltrastarDJ.Core.Game;
using UltrastarDJ.App.Localization;

namespace UltrastarDJ.App.ViewModels;

/// <summary>One player in the duet popup: sings voice 1, voice 2, or neither (<see cref="Out"/>, sits out).</summary>
public sealed partial class DuetSingerRow : ObservableObject
{
    public const int Out = -1;
    private readonly DuetSingersViewModel _owner;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVoice1), nameof(IsVoice2))]
    private int _voice;

    public DuetSingerRow(int playerId, string name, int voice, DuetSingersViewModel owner)
    {
        PlayerId = playerId;
        Name = name;
        _voice = voice;
        _owner = owner;
    }

    public int PlayerId { get; }
    public string Name { get; }
    public string ColorKey => $"BrushPlayer{PlayerId}";

    // On: this player takes the voice (from whoever had it). Off again (clicked while on): nobody sings it from here —
    // with one voice left, that player sings both. Neither on = sits out.
    public bool IsVoice1 { get => Voice == 0; set => Set(0, value); }
    public bool IsVoice2 { get => Voice == 1; set => Set(1, value); }

    private void Set(int voice, bool on)
    {
        if (on)
        {
            _owner.Give(this, voice);
        }
        else if (Voice == voice)
        {
            _owner.Give(this, Out);
        }
    }
}

/// <summary>An open display with players on it, and those players.</summary>
public sealed record DuetDisplaySection(string Title, IReadOnlyList<DuetSingerRow> Rows);

/// <summary>
/// The popup when a duet is loaded: per open display, its players (with a mic), each singing voice 1, voice 2 or
/// sitting out. Each voice has exactly one singer — giving a voice to one player takes it from whoever had it. Nobody
/// is moved between displays (that stays in Game Displays). Starts with the current pick (the first two set up).
/// </summary>
public sealed partial class DuetSingersViewModel : ObservableObject
{
    private readonly PlaybackService _playback;
    private readonly Action<bool> _close;

    /// <param name="playback">The loaded duet and who can sing it.</param>
    /// <param name="displays">Which displays are open and who is on them.</param>
    /// <param name="players">Names and mics.</param>
    /// <param name="close">Called with true on OK, false on Esc / the dimmed area.</param>
    public DuetSingersViewModel(PlaybackService playback, IDisplayService displays, PlayersService players, Action<bool> close)
    {
        _playback = playback;
        _close = close;
        IReadOnlyList<(int PlayerId, int Voice)> now = playback.Singers();
        // Singing both voices shows as voice 1 only: one voice picked = that player sings both.
        int VoiceOf(int id) => now.FirstOrDefault(s => s.PlayerId == id) is { PlayerId: > 0 } s
            ? (s.Voice == DuetVoices.BothVoices ? 0 : s.Voice)
            : DuetSingerRow.Out;

        List<DuetDisplaySection> sections = [];
        foreach (DisplayId id in (ReadOnlySpan<DisplayId>)[DisplayId.Beamer1, DisplayId.Beamer2])
        {
            if (!displays.IsOpen(id))
            {
                continue;
            }

            IReadOnlyList<int> assigned = displays.GetConfig(id).PlayerIds;
            List<DuetSingerRow> rows = [.. players.All.Where(p => p.Mic is not null && assigned.Contains(p.Id))
                .Select(p => new DuetSingerRow(p.Id, p.Name, VoiceOf(p.Id), this))];
            if (rows.Count > 0)
            {
                sections.Add(new DuetDisplaySection(L.F("duet.display_section", (int)id), rows));
            }
        }

        Sections = sections;
        Title = playback.Song?.Title ?? "";
        Voice1Label = playback.Song?.Voice1 is { Length: > 0 } a ? L.F("duet.voice1_named", a) : L.T("duet.voice1");
        Voice2Label = playback.Song?.Voice2 is { Length: > 0 } b ? L.F("duet.voice2_named", b) : L.T("duet.voice2");
    }

    public string Title { get; }
    public string Voice1Label { get; }
    public string Voice2Label { get; }
    public IReadOnlyList<DuetDisplaySection> Sections { get; }
    private IEnumerable<DuetSingerRow> Rows => Sections.SelectMany(s => s.Rows);

    /// <summary>Someone sings: both voices picked, or one — that player sings both.</summary>
    public bool IsComplete => Rows.Any(r => r.Voice != DuetSingerRow.Out);

    /// <summary>Only one player is set up: no table, just who sings both voices and how to make it a duet.</summary>
    public bool OnlyOnePlayer => Rows.Count() == 1;
    public bool ShowTable => !OnlyOnePlayer;

    /// <summary>"Player 1 sings both voices." while only one voice is picked; empty when both are.</summary>
    public string BothVoicesHint => Rows.Where(r => r.Voice != DuetSingerRow.Out).ToList() is [var only] ? L.F("duet.sings_both", only.Name) : "";

    public string OnlyOneText => Rows.FirstOrDefault() is { } r
        ? L.F("duet.only_one_player", r.Name)
        : "";

    /// <summary>A voice goes to one player only: whoever had it sits out.</summary>
    internal void Give(DuetSingerRow row, int voice)
    {
        if (voice != DuetSingerRow.Out)
        {
            foreach (DuetSingerRow other in Rows.Where(r => r != row && r.Voice == voice))
            {
                other.Voice = DuetSingerRow.Out;
            }
        }

        row.Voice = voice;
        OnPropertyChanged(nameof(IsComplete));
        OnPropertyChanged(nameof(BothVoicesHint));
        OkCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Swap()
    {
        foreach (DuetSingerRow r in Rows.Where(r => r.Voice != DuetSingerRow.Out))
        {
            r.Voice = 1 - r.Voice;
        }
    }

    [RelayCommand(CanExecute = nameof(IsComplete))]
    private void Ok()
    {
        DuetSingerRow? a = Rows.FirstOrDefault(r => r.Voice == 0);
        DuetSingerRow? b = Rows.FirstOrDefault(r => r.Voice == 1);
        if (a is not null && b is not null)
        {
            _playback.SetDuetChoice(new DuetChoice(a.PlayerId, b.PlayerId));
        }
        else if ((a ?? b) is { } only)
        {
            _playback.SetDuetChoice(DuetChoice.Solo(only.PlayerId));
        }

        _close(true);
    }

    [RelayCommand]
    private void Close() => _close(false);
}

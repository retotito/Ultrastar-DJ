using UltrastarDJ.Core.Abstractions;
using UltrastarDJ.Core.Players;

namespace UltrastarDJ.App.Services;

/// <summary>The four player slots, persisted. Single source of truth for names, colours, mics and per-mic settings.</summary>
public sealed class PlayersService
{
    private const string SettingsName = "players";
    private readonly ISettingsStore _settings;
    private PlayersDocument _doc;

    public PlayersService(ISettingsStore settings)
    {
        _settings = settings;
        _doc = settings.Load(SettingsName, PlayersDocument.Default());
        if (_doc.Players.Count != PlayerConfig.MaxPlayers)
        {
            _doc = PlayersDocument.Default();
        }
    }

    public event Action<PlayerConfig>? Changed;

    public IReadOnlyList<PlayerConfig> All => _doc.Players;
    public PlayerConfig Get(int id) => _doc.Players[id - 1];

    /// <summary>Players that have a mic bound (candidates for the game).</summary>
    public IEnumerable<PlayerConfig> WithMic => _doc.Players.Where(p => p.Mic is not null);

    public void Update(int id, Func<PlayerConfig, PlayerConfig> change)
    {
        PlayerConfig updated = change(Get(id));
        List<PlayerConfig> list = [.. _doc.Players];
        list[id - 1] = updated;
        _doc = new PlayersDocument(list);
        _settings.Save(SettingsName, _doc);
        Changed?.Invoke(updated);
    }

    public sealed record PlayersDocument(IReadOnlyList<PlayerConfig> Players)
    {
        public static PlayersDocument Default() => new(Enumerable.Range(1, PlayerConfig.MaxPlayers).Select(PlayerConfig.Default).ToList());
    }
}

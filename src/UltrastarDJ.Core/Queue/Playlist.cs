using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Queue;

/// <summary>The DJ's ordered playlist. Manually managed, manually started. Not thread-safe (UI thread only).</summary>
public sealed class Playlist
{
    private readonly List<Song> _items = [];

    public event Action? Changed;

    public IReadOnlyList<Song> Items => _items;
    public int ActiveIndex { get; private set; } = -1;
    public Song? ActiveSong => ActiveIndex >= 0 && ActiveIndex < _items.Count ? _items[ActiveIndex] : null;
    public int Count => _items.Count;

    public void Add(Song song)
    {
        _items.Add(song);
        Changed?.Invoke();
    }

    public bool Remove(string songId)
    {
        int idx = _items.FindIndex(s => s.Id == songId);
        if (idx < 0)
        {
            return false;
        }

        _items.RemoveAt(idx);
        if (ActiveIndex >= idx)
        {
            ActiveIndex = Math.Max(ActiveIndex - 1, -1);
        }

        Changed?.Invoke();
        return true;
    }

    public void MoveUp(string songId) => Swap(_items.FindIndex(s => s.Id == songId), -1);
    public void MoveDown(string songId) => Swap(_items.FindIndex(s => s.Id == songId), +1);

    private void Swap(int idx, int delta)
    {
        int other = idx + delta;
        if (idx < 0 || other < 0 || other >= _items.Count)
        {
            return;
        }

        (_items[idx], _items[other]) = (_items[other], _items[idx]);
        if (ActiveIndex == idx)
        {
            ActiveIndex = other;
        }
        else if (ActiveIndex == other)
        {
            ActiveIndex = idx;
        }

        Changed?.Invoke();
    }

    public void SetActive(string? songId)
    {
        ActiveIndex = songId is null ? -1 : _items.FindIndex(s => s.Id == songId);
        Changed?.Invoke();
    }

    /// <summary>The first song after the active one (or the first song when nothing is active).</summary>
    public Song? Next => ActiveIndex + 1 < _items.Count ? _items[ActiveIndex + 1] : null;

    public void Clear()
    {
        _items.Clear();
        ActiveIndex = -1;
        Changed?.Invoke();
    }
}

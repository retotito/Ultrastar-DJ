using UltrastarDJ.Core.Songs;

namespace UltrastarDJ.Core.Abstractions;

/// <summary>Persists <see cref="LoadFailure"/>s (in the library database, so they survive restarts and rescans).</summary>
public interface ILoadFailureStore
{
    IReadOnlyList<LoadFailure> All();
    void Save(LoadFailure failure);
    void Remove(string songId);
}

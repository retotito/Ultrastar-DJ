namespace UltrastarDJ.Core.Abstractions;

/// <summary>
/// Persists small, named settings documents (players, displays, outputs, app settings).
/// Implementations must be safe to call from the UI thread and must never throw on a missing document.
/// </summary>
public interface ISettingsStore
{
    /// <summary>Loads the document <paramref name="name"/>, or returns <paramref name="defaults"/> when absent or unreadable.</summary>
    T Load<T>(string name, T defaults) where T : class;

    /// <summary>Writes the document atomically (temp file + rename).</summary>
    void Save<T>(string name, T value) where T : class;
}

using ConsoleMode.Core;

namespace ConsoleMode.UI.Fluent;

/// <summary>
/// Saves a settings change made from the window, unless the page is filling its controls (which raises the same change
/// events). Every change is made and saved under the settings lock, as the network and gamepad threads read them.
/// </summary>
internal sealed class SettingsEditor
{
    private readonly AppSettings _settings;
    private int _loading;

    public SettingsEditor(AppSettings settings) => _settings = settings;

    public bool IsLoading => _loading > 0;

    /// <summary>Until disposed, changes are not saved. Nestable.</summary>
    public IDisposable BeginLoading()
    {
        _loading++;
        return new LoadingScope(this);
    }

    public void Save(Action change)
    {
        if (IsLoading)
        {
            return;
        }
        lock (_settings)
        {
            change();
            _settings.Save();
        }
    }

    private sealed class LoadingScope : IDisposable
    {
        private SettingsEditor? _editor;

        public LoadingScope(SettingsEditor editor) => _editor = editor;

        public void Dispose()
        {
            if (_editor != null)
            {
                _editor._loading--;
                _editor = null;
            }
        }
    }
}

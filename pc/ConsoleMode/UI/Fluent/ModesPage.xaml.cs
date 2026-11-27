using System.Windows;
using System.Windows.Controls;
using ConsoleMode.Core;
using ConsoleMode.Native;
using ComboBox = System.Windows.Controls.ComboBox;

namespace ConsoleMode.UI.Fluent;

/// <summary>Display and audio devices applied when entering each mode.</summary>
public partial class ModesPage : Page
{
    private readonly TrayApplicationContext _app;
    private readonly MainWindow _window;
    private readonly SettingsEditor _editor;
    private AppMode _shown;

    private AppSettings Settings => _app.Settings;

    public ModesPage(TrayApplicationContext app, MainWindow window)
    {
        _app = app;
        _window = window;
        _editor = new SettingsEditor(app.Settings);
        InitializeComponent();

        using (_editor.BeginLoading())
        {
            _shown = app.Switcher.State.Mode;
            (_shown == AppMode.Console ? ConsoleTab : PcTab).IsChecked = true;
        }
        LoadDeviceLists();

        DisplayCombo.SelectionChanged += (_, _) => _editor.Save(() =>
        {
            var o = DisplayCombo.SelectedItem as DeviceOption;
            var profile = Settings.ProfileFor(_shown);
            profile.DisplayId = o?.Id;
            profile.DisplayName = o?.Name;
        });
        AudioCombo.SelectionChanged += (_, _) => _editor.Save(() =>
        {
            var o = AudioCombo.SelectedItem as DeviceOption;
            var profile = Settings.ProfileFor(_shown);
            profile.AudioDeviceId = o?.Id;
            profile.AudioDeviceName = o?.Name;
        });
        MicCombo.SelectionChanged += (_, _) => _editor.Save(() =>
        {
            var o = MicCombo.SelectedItem as DeviceOption;
            var profile = Settings.ProfileFor(_shown);
            profile.MicrophoneId = o?.Id;
            profile.MicrophoneName = o?.Name;
        });
        CommsCombo.SelectionChanged += (_, _) => _editor.Save(() =>
        {
            var o = CommsCombo.SelectedItem as DeviceOption;
            var profile = Settings.ProfileFor(_shown);
            profile.CommunicationsId = o?.Id;
            profile.CommunicationsName = o?.Name;
        });
    }

    /// <summary>A display or audio device was connected or removed.</summary>
    public void RefreshDeviceLists() => LoadDeviceLists();

    public void UpdateModeState(ModeState state)
    {
        if (!state.Busy && state.Error == null)
        {
            LoadDeviceLists(); // the default display and audio have changed
        }
    }

    private void OnTabChecked(object sender, RoutedEventArgs e)
    {
        if (_editor.IsLoading)
        {
            return;
        }
        _shown = ConsoleTab.IsChecked == true ? AppMode.Console : AppMode.Pc;
        LoadDeviceLists();
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        LoadDeviceLists();
        _window.ShowStatus("Lista de pantallas y dispositivos de audio actualizada.");
    }

    private void LoadDeviceLists()
    {
        using var loading = _editor.BeginLoading();
        IReadOnlyList<DisplayInfo> displays;
        IReadOnlyList<AudioDeviceInfo> audio;
        IReadOnlyList<AudioDeviceInfo> mics;
        try
        {
            displays = DisplayManager.GetDisplays();
        }
        catch (Exception ex)
        {
            displays = Array.Empty<DisplayInfo>();
            _window.ShowStatus($"No se pudieron leer las pantallas: {ex.Message}", error: true);
        }
        try
        {
            audio = AudioManager.GetOutputDevices();
        }
        catch (Exception ex)
        {
            audio = Array.Empty<AudioDeviceInfo>();
            _window.ShowStatus($"No se pudieron leer los dispositivos de audio: {ex.Message}", error: true);
        }
        try
        {
            mics = AudioManager.GetInputDevices();
        }
        catch (Exception ex)
        {
            mics = Array.Empty<AudioDeviceInfo>();
            _window.ShowStatus($"No se pudieron leer los micrófonos: {ex.Message}", error: true);
        }

        var profile = Settings.ProfileFor(_shown);
        Fill(DisplayCombo, displays.Select(d => new DeviceOption(d.Id, d.Label, d.FriendlyName)),
            profile.DisplayId, profile.DisplayName, "no conectada");
        Fill(AudioCombo, audio.Select(a => new DeviceOption(a.Id, a.IsDefault ? $"{a.Name} · predeterminado" : a.Name, a.Name)),
            profile.AudioDeviceId, profile.AudioDeviceName, "no disponible");
        Fill(MicCombo, mics.Select(a => new DeviceOption(a.Id, a.IsDefault ? $"{a.Name} · predeterminado" : a.Name, a.Name)),
            profile.MicrophoneId, profile.MicrophoneName, "no disponible");
        Fill(CommsCombo, audio.Select(a => new DeviceOption(a.Id, a.Name, a.Name)),
            profile.CommunicationsId, profile.CommunicationsName, "no disponible");
    }

    private static void Fill(ComboBox combo, IEnumerable<DeviceOption> available, string? selectedId, string? savedName, string missingText)
    {
        var (items, selected) = DeviceOptions.Build(available, selectedId, savedName, missingText);
        combo.ItemsSource = items;
        combo.SelectedItem = selected;
    }
}

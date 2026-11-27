namespace ConsoleMode.Core;

/// <summary>An entry of a display or audio drop-down. <see cref="Id"/> null means «do not change».</summary>
public sealed record DeviceOption(string? Id, string Label, string? Name)
{
    public static readonly DeviceOption NoChange = new(null, "(No cambiar)", null);

    public override string ToString() => Label;
}

public static class DeviceOptions
{
    /// <summary>
    /// Entries of a drop-down: «(No cambiar)» first, then the available devices. A saved device that is not connected
    /// right now is kept as an extra entry («name (missing text)») so that the setting is not lost.
    /// </summary>
    public static (IReadOnlyList<DeviceOption> Items, DeviceOption Selected) Build(
        IEnumerable<DeviceOption> available, string? selectedId, string? savedName, string missingText)
    {
        var items = new List<DeviceOption> { DeviceOption.NoChange };
        items.AddRange(available);

        var selected = items.Skip(1).FirstOrDefault(o => o.Id == selectedId);
        if (selected == null && !string.IsNullOrEmpty(selectedId))
        {
            selected = new DeviceOption(selectedId, $"{savedName ?? selectedId} ({missingText})", savedName ?? selectedId);
            items.Add(selected);
        }
        return (items, selected ?? DeviceOption.NoChange);
    }
}

using ConsoleMode.Network;

namespace ConsoleMode.UI;

/// <summary>Diálogo "¿Confiar en este dispositivo?" que aparece al emparejar un móvil nuevo.</summary>
public sealed class PairRequestForm : Form
{
    public bool Accepted { get; private set; }

    public PairRequestForm(PairRequest request, Icon icon)
    {
        Text = "Console Mode · Nuevo dispositivo";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        Font = new Font("Segoe UI", 10f);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };

        layout.Controls.Add(new Label
        {
            Text = $"«{request.DeviceName}» quiere controlar este PC",
            Font = new Font("Segoe UI Semibold", 12f),
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Margin = new Padding(0, 0, 0, 8),
        });
        layout.Controls.Add(new Label
        {
            Text = $"Dirección: {request.RemoteAddress}\n\n" +
                   "Si confías en él, se guardará como dispositivo de confianza y no se volverá a preguntar. " +
                   "Podrás quitarlo cuando quieras en la pestaña «Móviles».",
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Margin = new Padding(0, 0, 0, 16),
        });

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
        var accept = new Button { Text = "Confiar", AutoSize = true, Padding = new Padding(12, 4, 12, 4) };
        var reject = new Button { Text = "Rechazar", AutoSize = true, Padding = new Padding(12, 4, 12, 4) };
        accept.Click += (_, _) =>
        {
            Accepted = true;
            Close();
        };
        reject.Click += (_, _) => Close();
        buttons.Controls.Add(accept);
        buttons.Controls.Add(reject);
        layout.Controls.Add(buttons);

        Controls.Add(layout);
        CancelButton = reject;
        // Por seguridad, el foco empieza en «Rechazar» para que un Enter accidental no empareje.
        Shown += (_, _) => reject.Focus();
    }
}

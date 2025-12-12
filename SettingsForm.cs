using System;
using System.Drawing;
using System.Windows.Forms;

namespace QuackDuck;

internal sealed class SettingsForm : Form
{
    private readonly NumericUpDown cursorHuntChance;
    private readonly NumericUpDown randomSoundChance;
    private readonly Action<PetSettingsSnapshot> onApply;

    internal SettingsForm(PetSettingsSnapshot snapshot, Action<PetSettingsSnapshot> onApply)
    {
        this.onApply = onApply ?? throw new ArgumentNullException(nameof(onApply));

        Text = "QuackDuck Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(460, 320);
        BackColor = Color.FromArgb(28, 28, 32);
        ForeColor = Color.WhiteSmoke;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 4,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new Label
        {
            Text = "Pet Settings",
            Font = new Font(Font.FontFamily, 14, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(header, 0, 0);

        cursorHuntChance = CreateNumeric("Cursor Hunt Chance (%)", snapshot.CursorHuntChancePercent, out var cursorPanel);
        randomSoundChance = CreateNumeric("Random Sound Chance (%)", snapshot.RandomSoundChancePercent, out var soundPanel);

        root.Controls.Add(cursorPanel, 0, 1);
        root.Controls.Add(soundPanel, 0, 2);
        root.Controls.Add(BuildButtons(), 0, 3);
        Controls.Add(root);
    }

    private Control BuildButtons()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 12, 0, 0)
        };

        var save = new Button
        {
            Text = "Save",
            BackColor = Color.FromArgb(208, 116, 255),
            ForeColor = Color.Black,
            FlatStyle = FlatStyle.Flat,
            Width = 100
        };
        save.FlatAppearance.BorderSize = 0;
        save.Click += (_, _) =>
        {
            var snapshot = new PetSettingsSnapshot
            {
                CursorHuntChancePercent = (int)cursorHuntChance.Value,
                RandomSoundChancePercent = (int)randomSoundChance.Value
            };
            onApply(snapshot);
            DialogResult = DialogResult.OK;
            Close();
        };

        var cancel = new Button
        {
            Text = "Cancel",
            BackColor = Color.FromArgb(46, 46, 52),
            ForeColor = Color.WhiteSmoke,
            FlatStyle = FlatStyle.Flat,
            Width = 100
        };
        cancel.FlatAppearance.BorderSize = 0;
        cancel.Click += (_, _) => Close();

        panel.Controls.Add(save);
        panel.Controls.Add(cancel);
        return panel;
    }

    private NumericUpDown CreateNumeric(string label, int value, out Control panel)
    {
        var container = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill
        };
        container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        container.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        var lbl = new Label
        {
            Text = label,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var numeric = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp(value, 0, 100),
            BackColor = Color.FromArgb(36, 36, 42),
            ForeColor = Color.WhiteSmoke,
            BorderStyle = BorderStyle.FixedSingle
        };

        container.Controls.Add(lbl, 0, 0);
        container.Controls.Add(numeric, 1, 0);
        panel = container;
        return numeric;
    }
}

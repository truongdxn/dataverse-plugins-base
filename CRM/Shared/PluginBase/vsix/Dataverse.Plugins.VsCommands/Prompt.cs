using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualStudio.PlatformUI;

namespace Dataverse.Plugins.VsCommands;

internal enum PromptKind
{
    Text,
    Choice,
    Check,
}

/// <summary>One thing to ask for. Maps to exactly one dv option.</summary>
internal sealed class PromptField
{
    public PromptField(string label, string value = null, PromptKind kind = PromptKind.Text, IReadOnlyList<string> choices = null, string hint = null)
    {
        Label = label;
        Value = value ?? string.Empty;
        Kind = kind;
        Choices = choices ?? new List<string>();
        Hint = hint;
    }

    public string Label { get; }

    public PromptKind Kind { get; }

    public IReadOnlyList<string> Choices { get; }

    /// <summary>Shown under the field. Used for the things that cannot be undone.</summary>
    public string Hint { get; }

    public string Value { get; set; }

    public bool Checked { get; set; }
}

/// <summary>
/// The one dialog, built in code.
/// <para>
/// Every command that needs input needs the same shape: a few labelled values and an OK button.
/// Built without XAML deliberately - a generated .g.cs, a build action and a resource lookup are
/// three more things to go wrong in an extension that gets debugged on somebody else's machine.
/// </para>
/// </summary>
internal static class Prompt
{
    public static bool TryAsk(string title, IList<PromptField> fields)
    {
        var window = new DialogWindow
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            HasMaximizeButton = false,
            HasMinimizeButton = false,
        };

        var layout = new StackPanel { Margin = new Thickness(16) };
        var editors = new List<Action>();

        foreach (var field in fields)
        {
            var captured = field;

            layout.Children.Add(new TextBlock
            {
                Text = captured.Label,
                Margin = new Thickness(0, 0, 0, 4),
            });

            switch (captured.Kind)
            {
                case PromptKind.Choice:
                {
                    var combo = new ComboBox { IsEditable = true, Margin = new Thickness(0, 0, 0, 12) };

                    foreach (var choice in captured.Choices)
                    {
                        combo.Items.Add(choice);
                    }

                    combo.Text = captured.Value;
                    layout.Children.Add(combo);
                    editors.Add(() => captured.Value = combo.Text);
                    break;
                }

                case PromptKind.Check:
                {
                    var check = new CheckBox { IsChecked = captured.Checked, Margin = new Thickness(0, 0, 0, 12) };
                    layout.Children.Add(check);
                    editors.Add(() => captured.Checked = check.IsChecked == true);
                    break;
                }

                default:
                {
                    var text = new TextBox { Text = captured.Value, Margin = new Thickness(0, 0, 0, 12) };
                    layout.Children.Add(text);
                    editors.Add(() => captured.Value = text.Text);
                    break;
                }
            }

            if (!string.IsNullOrEmpty(captured.Hint))
            {
                layout.Children.Add(new TextBlock
                {
                    Text = captured.Hint,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.75,
                    Margin = new Thickness(0, -8, 0, 12),
                });
            }
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var ok = new Button { Content = "Run", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };

        ok.Click += (_, __) =>
        {
            foreach (var apply in editors)
            {
                apply();
            }

            window.DialogResult = true;
        };

        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        layout.Children.Add(buttons);

        window.Content = layout;

        return window.ShowModal() == true;
    }
}

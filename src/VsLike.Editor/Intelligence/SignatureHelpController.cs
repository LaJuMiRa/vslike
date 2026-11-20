using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using VsLike.Core.Code;

namespace VsLike.Editor.Intelligence;

/// <summary>Parameterhilfe über AvaloniaEdits <see cref="OverloadInsightWindow"/>; der aktive Parameter ist fett.</summary>
public sealed class SignatureHelpController
{
    private readonly TextEditor _editor;
    private readonly Provider _provider = new();
    private OverloadInsightWindow? _window;

    public SignatureHelpController(TextEditor editor)
    {
        _editor = editor;
    }

    public bool IsOpen => _window is not null;

    public SignatureHelp? Current => _provider.Help;

    public void Show(SignatureHelp help)
    {
        _provider.Update(help);
        if (_window is not null)
        {
            return;
        }

        var window = new OverloadInsightWindow(_editor.TextArea) { Provider = _provider };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_window, window))
            {
                _window = null;
            }
        };
        _window = window;
        window.Show();
    }

    public void Close()
    {
        _window?.Close();
        _window = null;
    }

    private sealed class Provider : IOverloadProvider
    {
        private int _selectedIndex;

        public event PropertyChangedEventHandler? PropertyChanged;

        public SignatureHelp? Help { get; private set; }

        public int SelectedIndex
        {
            get
            {
                return _selectedIndex;
            }

            set
            {
                _selectedIndex = Math.Clamp(value, 0, Math.Max(0, Count - 1));
                Raise();
            }
        }

        public int Count => Help?.Signatures.Count ?? 0;

        public string CurrentIndexText => Count > 1 ? $"{SelectedIndex + 1}/{Count}" : string.Empty;

        public object CurrentHeader
        {
            get
            {
                var block = new TextBlock { FontFamily = new FontFamily("JetBrains Mono, Menlo, monospace"), TextWrapping = TextWrapping.Wrap, MaxWidth = 640 };
                if (Help is null || Count == 0)
                {
                    return block;
                }

                var signature = Help.Signatures[SelectedIndex];
                var inlines = new InlineCollection { new Run(signature.Prefix) };
                for (var i = 0; i < signature.Parameters.Count; i++)
                {
                    if (i > 0)
                    {
                        inlines.Add(new Run(", "));
                    }

                    var active = i == Help.ActiveParameter || (i == signature.Parameters.Count - 1 && Help.ActiveParameter > i && signature.Parameters[i].StartsWith("params ", StringComparison.Ordinal));
                    inlines.Add(new Run(signature.Parameters[i]) { FontWeight = active ? FontWeight.Bold : FontWeight.Normal });
                }

                inlines.Add(new Run(signature.Suffix));
                block.Inlines = inlines;
                return block;
            }
        }

        public object CurrentContent => Help is { } help && Count > 0 ? help.Signatures[SelectedIndex].Documentation ?? string.Empty : string.Empty;

        public void Update(SignatureHelp help)
        {
            var keepSelection = Help is not null && Help.Signatures.Count == help.Signatures.Count;
            Help = help;
            _selectedIndex = keepSelection ? Math.Min(_selectedIndex, Count - 1) : Math.Max(0, help.ActiveSignature);
            Raise();
        }

        private void Raise()
        {
            foreach (var name in new[] { nameof(SelectedIndex), nameof(Count), nameof(CurrentIndexText), nameof(CurrentHeader), nameof(CurrentContent) })
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }
    }
}

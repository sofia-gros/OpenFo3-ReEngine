using Godot;
using System;
using System.Collections.Generic;

namespace OpenFo3.World
{
    public partial class WorldSelectMenu : CanvasLayer
    {
        private VBoxContainer _listContainer;
        private Label _titleLabel;
        private List<string> _worldNames;
        private Action<string> _onWorldSelected;
        private bool _isOpen = false;

        public override void _Ready()
        {
            Layer = 200;
            Visible = false;
            CreateUI();
        }

        private void CreateUI()
        {
            var panel = new Panel();
            panel.Size = new Vector2(400, 600);
            panel.Position = new Vector2(100, 100);
            panel.Modulate = new Color(0, 0, 0, 0.85f);
            AddChild(panel);

            _titleLabel = new Label();
            _titleLabel.Position = new Vector2(20, 15);
            _titleLabel.Text = "Select World (click to load)  [P] to close";
            _titleLabel.LabelSettings = new LabelSettings
            {
                FontSize = 16,
                FontColor = new Color(1, 1, 0.6f),
                OutlineSize = 1,
                OutlineColor = new Color(0, 0, 0),
            };
            panel.AddChild(_titleLabel);

            var scroll = new ScrollContainer();
            scroll.Position = new Vector2(10, 45);
            scroll.Size = new Vector2(380, 540);
            panel.AddChild(scroll);

            _listContainer = new VBoxContainer();
            _listContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            scroll.AddChild(_listContainer);
        }

        public void LoadWorlds(List<string> worldNames, Action<string> onSelected)
        {
            _worldNames = worldNames;
            _onWorldSelected = onSelected;

            foreach (var child in _listContainer.GetChildren())
            {
                _listContainer.RemoveChild(child);
                child.QueueFree();
            }

            for (int i = 0; i < worldNames.Count; i++)
            {
                int idx = i;
                var name = worldNames[i];

                var btn = new Button();
                btn.Text = $"[{idx + 1}] {name}";
                btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                btn.CustomMinimumSize = new Vector2(0, 30);

                int capturedIdx = idx;
                string capturedName = name;
                btn.Pressed += () =>
                {
                    _onWorldSelected?.Invoke(capturedName);
                    Close();
                };

                _listContainer.AddChild(btn);
            }
        }

        public void Toggle()
        {
            if (_isOpen) Close();
            else Open();
        }

        public void Open()
        {
            _isOpen = true;
            Visible = true;
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }

        public void Close()
        {
            _isOpen = false;
            Visible = false;
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }

        public bool IsOpen => _isOpen;
    }
}

using System;
using System.Drawing;
using System.Windows.Forms;

namespace KeyboardDebounce
{
    internal abstract class SettingsPageBase : UserControl
    {
        private bool _refreshing;

        protected SettingsPageBase(SettingsUiContext context)
        {
            if (context == null) throw new ArgumentNullException("context");
            Context = context;
            Dock = DockStyle.Fill;
            BackColor = SystemColors.Control;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScroll = true;
        }

        protected SettingsUiContext Context { get; private set; }

        internal abstract SettingsPageId PageId { get; }
        internal abstract string PageTitle { get; }
        internal virtual Control InitialFocusControl
        {
            get { return this; }
        }

        protected bool IsRefreshing
        {
            get { return _refreshing; }
        }

        public abstract void RefreshData();

        public virtual void RefreshLiveState()
        {
        }

        protected void RunRefresh(Action action)
        {
            _refreshing = true;
            try
            {
                action();
            }
            finally
            {
                _refreshing = false;
            }
        }

        protected Control CreateVerticalRoot()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 0,
                Padding = new Padding(24),
                Margin = new Padding(0)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            Controls.Add(panel);
            return panel;
        }

        protected void AddRootRow(TableLayoutPanel root, Control control)
        {
            control.Margin = new Padding(0, 0, 0, 16);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(control, 0, root.RowCount);
            root.RowCount += 1;
        }

        protected SettingsCardHandle CreateCard(string title, string description)
        {
            var card = new SettingsCard(title, description);
            return new SettingsCardHandle(card, card.Content);
        }

        protected static Label CreateLabel(string text)
        {
            return new Label
            {
                AutoSize = true,
                Text = text,
                Margin = new Padding(0, 0, 0, 6)
            };
        }

        protected static Label CreateValueLabel()
        {
            return new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6)
            };
        }

        protected static Label CreatePathValueLabel()
        {
            return new Label
            {
                AutoSize = false,
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                MinimumSize = new Size(0, 24),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 0, 6)
            };
        }

        protected static Button CreateButton(string text, EventHandler onClick)
        {
            var button = new Button
            {
                AutoSize = true,
                Text = text,
                Margin = new Padding(0, 0, 8, 0)
            };
            button.Click += onClick;
            return button;
        }

        protected static void PreserveEditorWidth(Control control, int logicalWidth)
        {
            if (control == null) throw new ArgumentNullException("control");
            if (logicalWidth <= 0) throw new ArgumentOutOfRangeException("logicalWidth");

            control.MinimumSize = new Size(logicalWidth, control.MinimumSize.Height);
            control.Width = logicalWidth;
        }

        protected static FlowLayoutPanel CreateButtonRow()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0)
            };
        }

        protected static TableLayoutPanel CreateSingleColumnPanel()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 0,
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            return layout;
        }

        protected static TableLayoutPanel CreateTwoColumnForm()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 0,
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            return layout;
        }

        protected static void AddFormRow(TableLayoutPanel layout, string labelText, Control control)
        {
            var label = new Label
            {
                AutoSize = true,
                Text = labelText,
                Margin = new Padding(0, 6, 12, 6)
            };
            if (String.IsNullOrWhiteSpace(control.AccessibleName))
            {
                control.AccessibleName = labelText;
            }
            control.TabIndex = layout.RowCount;
            control.Margin = new Padding(0, 3, 0, 6);
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(label, 0, layout.RowCount);
            layout.Controls.Add(control, 1, layout.RowCount);
            layout.RowCount += 1;
        }

        protected static string KeyName(int virtualKeyCode)
        {
            string keyName = ((Keys)virtualKeyCode).ToString();
            return String.IsNullOrEmpty(keyName) ? "VK " + virtualKeyCode : keyName;
        }

        protected int GetEffectiveThresholdForDisplay(KeyLearningState state)
        {
            if (state == null) return 0;

            int value = (int)Math.Round(state.ThresholdMs * Context.Settings.GlobalSensitivity);
            if (value < 20) return 20;
            if (value > 250) return 250;
            return value;
        }

        protected sealed class SettingsCardHandle
        {
            public SettingsCardHandle(Control shell, Control content)
            {
                Shell = shell;
                Content = content;
            }

            public Control Shell { get; private set; }
            public Control Content { get; private set; }
        }
    }
}

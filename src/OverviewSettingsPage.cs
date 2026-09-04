using System;
using System.Windows.Forms;

namespace KeyboardDebounce
{
    internal sealed class OverviewSettingsPage : SettingsPageBase
    {
        private readonly Label _enabledValue;
        private readonly Label _recentEventValue;
        private readonly Label _gameModeValue;
        private readonly Label _processCountValue;
        private readonly Label _normalConfigValue;
        private readonly Label _learnedKeyCountValue;
        private readonly Label _ignoredCountValue;
        private readonly Label _gameFilteredCountValue;
        private readonly Label _configPathValue;
        private readonly Button _goNormal;

        public OverviewSettingsPage(SettingsUiContext context)
            : base(context)
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(24),
                Margin = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var statusCard = CreateCard("运行状态", "当前模式、游戏 EXE 状态和最近事件");
            var statusLayout = CreateTwoColumnForm();
            statusCard.Content.Controls.Add(statusLayout);
            _enabledValue = CreateValueLabel();
            _recentEventValue = CreatePathValueLabel();
            _gameModeValue = CreatePathValueLabel();
            _processCountValue = CreateValueLabel();
            AddFormRow(statusLayout, "防抖状态", _enabledValue);
            AddFormRow(statusLayout, "最近事件", _recentEventValue);
            AddFormRow(statusLayout, "游戏模式", _gameModeValue);
            AddFormRow(statusLayout, "游戏 EXE 数", _processCountValue);

            var summaryCard = CreateCard("配置摘要", "普通参数、已学习按键和例外列表");
            var summaryLayout = CreateTwoColumnForm();
            summaryCard.Content.Controls.Add(summaryLayout);
            _learnedKeyCountValue = CreateValueLabel();
            _normalConfigValue = CreatePathValueLabel();
            _ignoredCountValue = CreateValueLabel();
            _gameFilteredCountValue = CreateValueLabel();
            _configPathValue = CreatePathValueLabel();
            AddFormRow(summaryLayout, "普通参数", _normalConfigValue);
            AddFormRow(summaryLayout, "已学习按键", _learnedKeyCountValue);
            AddFormRow(summaryLayout, "始终忽略", _ignoredCountValue);
            AddFormRow(summaryLayout, "游戏防抖键", _gameFilteredCountValue);
            AddFormRow(summaryLayout, "配置目录", _configPathValue);

            var shortcutCard = CreateCard("快捷跳转", "直接进入常用功能页");
            var actions = CreateButtonRow();
            actions.WrapContents = false;
            _goNormal = CreateButton("普通防抖", delegate { Context.RequestNavigation(SettingsPageId.NormalDebounce); });
            actions.Controls.Add(_goNormal);
            actions.Controls.Add(CreateButton("游戏模式", delegate { Context.RequestNavigation(SettingsPageId.GameMode); }));
            actions.Controls.Add(CreateButton("按键管理", delegate { Context.RequestNavigation(SettingsPageId.KeyManagement); }));
            actions.Controls.Add(CreateButton("应用设置", delegate { Context.RequestNavigation(SettingsPageId.ApplicationSettings); }));
            actions.Controls.Add(CreateButton("打开配置目录", OnOpenConfigDirectory));
            shortcutCard.Content.Controls.Add(actions);
            statusCard.Shell.Dock = DockStyle.Fill;
            statusCard.Shell.Margin = new Padding(0, 0, 8, 16);
            summaryCard.Shell.Dock = DockStyle.Fill;
            summaryCard.Shell.Margin = new Padding(8, 0, 0, 16);
            shortcutCard.Shell.Dock = DockStyle.Top;
            shortcutCard.Shell.Margin = new Padding(0);
            root.Controls.Add(statusCard.Shell, 0, 0);
            root.Controls.Add(summaryCard.Shell, 1, 0);
            root.Controls.Add(shortcutCard.Shell, 0, 1);
            root.SetColumnSpan(shortcutCard.Shell, 2);

            RefreshData();
        }

        internal override SettingsPageId PageId
        {
            get { return SettingsPageId.Overview; }
        }

        internal override string PageTitle
        {
            get { return "概览"; }
        }

        internal override Control InitialFocusControl
        {
            get { return _goNormal; }
        }

        public override void RefreshData()
        {
            RunRefresh(delegate
            {
                Context.Settings.Normalize();
                _enabledValue.Text = Context.Settings.Enabled ? "已启用" : "已暂停";
                _recentEventValue.Text = Context.GetCurrentStatus();
                _gameModeValue.Text = Context.GetCurrentGameModeStatus();
                _processCountValue.Text = (Context.Settings.GameProcesses == null ? 0 : Context.Settings.GameProcesses.Count).ToString();
                _normalConfigValue.Text = String.Format(
                    "敏感度 {0:0.00}，默认阈值 {1}ms，长按 {2}ms",
                    Context.Settings.GlobalSensitivity,
                    Context.Settings.DefaultThresholdMs,
                    Context.Settings.LongHoldBypassMs);
                _learnedKeyCountValue.Text = Context.Learning.Keys.Count.ToString();
                _ignoredCountValue.Text = (Context.Settings.IgnoredKeys == null ? 0 : Context.Settings.IgnoredKeys.Count).ToString();
                _gameFilteredCountValue.Text = (Context.Settings.GameModeFilteredKeys == null ? 0 : Context.Settings.GameModeFilteredKeys.Count).ToString();
                _configPathValue.Text = Context.GetSettingsDirectory();
                _configPathValue.AccessibleDescription = _configPathValue.Text;
            });
        }

        public override void RefreshLiveState()
        {
            _recentEventValue.Text = Context.GetCurrentStatus();
            _gameModeValue.Text = Context.GetCurrentGameModeStatus();
        }

        private void OnOpenConfigDirectory(object sender, EventArgs e)
        {
            try
            {
                Context.OpenSettingsDirectory();
            }
            catch (Exception error)
            {
                MessageBox.Show(
                    error.Message,
                    FindForm() == null ? "Keyboard Debounce" : FindForm().Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
    }
}

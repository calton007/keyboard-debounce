using System;
using System.Windows.Forms;

namespace KeyboardDebounce
{
    internal sealed class NormalDebounceSettingsPage : SettingsPageBase
    {
        private readonly TrackBar _sensitivity;
        private readonly Label _sensitivityValue;
        private readonly NumericUpDown _defaultThreshold;
        private readonly NumericUpDown _longHoldBypass;

        public NormalDebounceSettingsPage(SettingsUiContext context)
            : base(context)
        {
            var root = (TableLayoutPanel)CreateVerticalRoot();

            var parameterCard = CreateCard("普通模式参数", "调整全局敏感度、阈值和长按放行");
            var parameterLayout = CreateTwoColumnForm();
            parameterCard.Content.Controls.Add(parameterLayout);

            _sensitivity = new TrackBar
            {
                Minimum = 50,
                Maximum = 300,
                TickFrequency = 25,
                AccessibleName = "全局敏感度",
                AccessibleDescription = "范围 0.50 到 3.00，使用方向键调整。"
            };
            PreserveEditorWidth(_sensitivity, 220);
            _sensitivity.ValueChanged += OnSensitivityChanged;
            var sensitivityWrap = CreateSingleColumnPanel();
            _sensitivityValue = CreateValueLabel();
            sensitivityWrap.Controls.Add(_sensitivity);
            sensitivityWrap.Controls.Add(_sensitivityValue);
            AddFormRow(parameterLayout, "全局敏感度", sensitivityWrap);

            _defaultThreshold = new NumericUpDown
            {
                Minimum = 20,
                Maximum = 250
            };
            PreserveEditorWidth(_defaultThreshold, 90);
            _defaultThreshold.ValueChanged += OnDefaultThresholdChanged;
            AddFormRow(parameterLayout, "默认阈值(ms)", _defaultThreshold);

            _longHoldBypass = new NumericUpDown
            {
                Minimum = 250,
                Maximum = 1000,
                Increment = 10
            };
            PreserveEditorWidth(_longHoldBypass, 90);
            _longHoldBypass.ValueChanged += OnLongHoldChanged;
            AddFormRow(parameterLayout, "长按放行(ms)", _longHoldBypass);
            AddRootRow(root, parameterCard.Shell);

            var presetCard = CreateCard("快捷操作", "按批准方案保留激进预设，不覆盖学习结果");
            var actions = CreateButtonRow();
            actions.Controls.Add(CreateButton("激进预设（不改学习）", OnApplyAggressivePreset));
            presetCard.Content.Controls.Add(actions);
            presetCard.Content.Controls.Add(CreateLabel("预设会设置普通模式默认阈值 160ms、长按放行 650ms、全局敏感度 1.50。"));
            AddRootRow(root, presetCard.Shell);

            var explanationCard = CreateCard("学习机制说明", "普通模式学习结果仍以每个按键为单位持久化");
            explanationCard.Content.Controls.Add(CreateLabel("普通模式下，按键会持续学习并更新阈值。游戏模式只限制行为，不会覆盖这里的配置。"));
            AddRootRow(root, explanationCard.Shell);

            RefreshData();
        }

        internal override SettingsPageId PageId
        {
            get { return SettingsPageId.NormalDebounce; }
        }

        internal override string PageTitle
        {
            get { return "普通防抖"; }
        }

        internal override Control InitialFocusControl
        {
            get { return _sensitivity; }
        }

        public override void RefreshData()
        {
            RunRefresh(delegate
            {
                Context.Settings.Normalize();
                _sensitivity.Value = (int)Math.Round(Context.Settings.GlobalSensitivity * 100.0);
                _sensitivityValue.Text = Context.Settings.GlobalSensitivity.ToString("0.00");
                _defaultThreshold.Value = Context.Settings.DefaultThresholdMs;
                _longHoldBypass.Value = Context.Settings.LongHoldBypassMs;
            });
        }

        private void OnSensitivityChanged(object sender, EventArgs e)
        {
            if (IsRefreshing) return;
            Context.Settings.GlobalSensitivity = _sensitivity.Value / 100.0;
            _sensitivityValue.Text = Context.Settings.GlobalSensitivity.ToString("0.00");
            Context.SaveSettingsAndRefresh();
        }

        private void OnDefaultThresholdChanged(object sender, EventArgs e)
        {
            if (IsRefreshing) return;
            Context.Settings.DefaultThresholdMs = (int)_defaultThreshold.Value;
            Context.SaveSettingsAndRefresh();
        }

        private void OnLongHoldChanged(object sender, EventArgs e)
        {
            if (IsRefreshing) return;
            Context.Settings.LongHoldBypassMs = (int)_longHoldBypass.Value;
            Context.SaveSettingsAndRefresh();
        }

        private void OnApplyAggressivePreset(object sender, EventArgs e)
        {
            DialogResult confirm = MessageBox.Show(
                "确认应用普通模式激进预设？这只修改普通模式参数，不覆盖已有按键学习阈值。",
                FindForm() == null ? "Keyboard Debounce" : FindForm().Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            Context.Settings.DefaultThresholdMs = 160;
            Context.Settings.LongHoldBypassMs = 650;
            Context.Settings.GlobalSensitivity = 1.5;
            Context.SaveSettingsAndRefresh();
        }
    }
}

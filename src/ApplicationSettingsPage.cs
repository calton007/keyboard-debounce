using System;
using System.Windows.Forms;

namespace KeyboardDebounce
{
    internal sealed class ApplicationSettingsPage : SettingsPageBase
    {
        private readonly CheckBox _startup;
        private readonly CheckBox _silentRun;
        private readonly NumericUpDown _startupDelay;
        private readonly TextBox _pauseHotkey;
        private readonly Label _settingsPathValue;
        private readonly Label _learningPathValue;
        private readonly Label _adminStateValue;

        public ApplicationSettingsPage(SettingsUiContext context)
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

            var startupCard = CreateCard("启动与显示", "系统启动、自启和静默运行");
            var startupLayout = CreateTwoColumnForm();
            startupCard.Content.Controls.Add(startupLayout);
            _startup = new CheckBox { AutoSize = true, Text = "开机自启" };
            _startup.CheckedChanged += OnStartupChanged;
            _silentRun = new CheckBox { AutoSize = true, Text = "静默运行" };
            _silentRun.CheckedChanged += OnSilentRunChanged;
            _startupDelay = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 30000,
                Increment = 100
            };
            PreserveEditorWidth(_startupDelay, 100);
            _startupDelay.ValueChanged += OnStartupDelayChanged;
            AddFormRow(startupLayout, "自启", _startup);
            AddFormRow(startupLayout, "静默运行", _silentRun);
            AddFormRow(startupLayout, "启动延迟(ms)", _startupDelay);

            var hotkeyCard = CreateCard("暂停热键", "修改后需要单独应用");
            var hotkeyLayout = CreateTwoColumnForm();
            hotkeyCard.Content.Controls.Add(hotkeyLayout);
            _pauseHotkey = new TextBox();
            PreserveEditorWidth(_pauseHotkey, 180);
            AddFormRow(hotkeyLayout, "热键", _pauseHotkey);
            var hotkeyButtons = CreateButtonRow();
            hotkeyButtons.Controls.Add(CreateButton("应用热键", OnApplyHotkey));
            hotkeyCard.Content.Controls.Add(hotkeyButtons);

            var diagnosticsCard = CreateCard("配置与权限", "查看配置目录和当前权限状态");
            var diagnosticsLayout = CreateTwoColumnForm();
            diagnosticsCard.Content.Controls.Add(diagnosticsLayout);
            _settingsPathValue = CreatePathValueLabel();
            _learningPathValue = CreatePathValueLabel();
            _adminStateValue = CreateValueLabel();
            AddFormRow(diagnosticsLayout, "设置文件", _settingsPathValue);
            AddFormRow(diagnosticsLayout, "学习文件", _learningPathValue);
            AddFormRow(diagnosticsLayout, "权限状态", _adminStateValue);
            var diagnosticsButtons = CreateButtonRow();
            diagnosticsButtons.Controls.Add(CreateButton("打开配置目录", OnOpenConfigDirectory));
            diagnosticsButtons.Controls.Add(CreateButton("刷新权限状态", OnRefreshAdminStatus));
            diagnosticsCard.Content.Controls.Add(diagnosticsButtons);
            startupCard.Shell.Dock = DockStyle.Fill;
            startupCard.Shell.Margin = new Padding(0, 0, 8, 16);
            hotkeyCard.Shell.Dock = DockStyle.Fill;
            hotkeyCard.Shell.Margin = new Padding(8, 0, 0, 16);
            diagnosticsCard.Shell.Dock = DockStyle.Top;
            diagnosticsCard.Shell.Margin = new Padding(0);
            root.Controls.Add(startupCard.Shell, 0, 0);
            root.Controls.Add(hotkeyCard.Shell, 1, 0);
            root.Controls.Add(diagnosticsCard.Shell, 0, 1);
            root.SetColumnSpan(diagnosticsCard.Shell, 2);

            RefreshData();
        }

        internal override SettingsPageId PageId
        {
            get { return SettingsPageId.ApplicationSettings; }
        }

        internal override string PageTitle
        {
            get { return "应用设置"; }
        }

        internal override Control InitialFocusControl
        {
            get { return _startup; }
        }

        public override void RefreshData()
        {
            RunRefresh(delegate
            {
                Context.Settings.Normalize();
                _startup.Checked = Context.Settings.StartWithWindows;
                _silentRun.Checked = Context.Settings.SilentRun;
                _startupDelay.Value = Context.Settings.StartupDelayMs;
                _pauseHotkey.Text = Context.Settings.PauseHotkey;
                _settingsPathValue.Text = Context.Store.SettingsPath;
                _learningPathValue.Text = Context.Store.LearningStatePath;
                _settingsPathValue.AccessibleDescription = _settingsPathValue.Text;
                _learningPathValue.AccessibleDescription = _learningPathValue.Text;
                _adminStateValue.Text = GetAdminText();
            });
        }

        private void OnStartupChanged(object sender, EventArgs e)
        {
            if (IsRefreshing) return;
            Context.Settings.StartWithWindows = _startup.Checked;
            Context.SaveSettingsAndRefresh();
        }

        private void OnSilentRunChanged(object sender, EventArgs e)
        {
            if (IsRefreshing) return;
            Context.Settings.SilentRun = _silentRun.Checked;
            Context.SaveSettingsAndRefresh();
        }

        private void OnStartupDelayChanged(object sender, EventArgs e)
        {
            if (IsRefreshing) return;
            Context.Settings.StartupDelayMs = (int)_startupDelay.Value;
            Context.SaveSettingsAndRefresh();
        }

        private void OnApplyHotkey(object sender, EventArgs e)
        {
            Context.Settings.PauseHotkey = _pauseHotkey.Text == null ? "" : _pauseHotkey.Text.Trim();
            Context.SaveSettingsAndRefresh();
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

        private void OnRefreshAdminStatus(object sender, EventArgs e)
        {
            _adminStateValue.Text = GetAdminText();
        }

        private static string GetAdminText()
        {
            return NativeMethods.IsUserAnAdmin()
                ? "当前已使用管理员权限运行"
                : "当前为普通权限；管理员窗口可能无法完整覆盖";
        }
    }
}

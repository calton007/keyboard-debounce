using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Automation;

namespace KeyboardDebounce
{
    internal sealed class RunningExecutablePickerDialog : Form
    {
        private readonly Func<IReadOnlyList<string>> _loadExecutables;
        private readonly HashSet<string> _excluded;
        private readonly HashSet<string> _selected;
        private readonly List<string> _candidates;
        private readonly TextBox _search;
        private readonly CheckedListBox _list;
        private readonly Label _status;
        private readonly Button _add;
        private readonly Button _refresh;
        private readonly Font _dialogFont;
        private readonly Icon _dialogIcon;
        private bool _updatingList;
        private bool _loading;
        private bool _disposed;

        public RunningExecutablePickerDialog(
            IEnumerable<string> excludedExecutables,
            Func<IReadOnlyList<string>> loadExecutables)
        {
            if (loadExecutables == null) throw new ArgumentNullException("loadExecutables");

            _loadExecutables = loadExecutables;
            _excluded = NormalizeSet(excludedExecutables);
            _selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _candidates = new List<string>();

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            _dialogFont = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            _dialogIcon = AppIcon.Load();
            Font = _dialogFont;
            Text = "从正在运行的程序添加";
            Icon = _dialogIcon;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(620, 520);
            MinimumSize = new Size(520, 420);
            ShowInTaskbar = false;
            MinimizeBox = false;
            Name = "RunningExecutablePickerDialog";

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(16),
                Margin = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var introduction = new Label
            {
                AutoSize = true,
                Text = "勾选要添加的 EXE。这里只列出当前正在运行的程序，不会自动判断哪些是游戏。",
                Margin = new Padding(0, 0, 0, 12)
            };
            root.Controls.Add(introduction, 0, 0);

            var searchRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 8)
            };
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            var searchLabel = new Label
            {
                AutoSize = true,
                Text = "搜索",
                Margin = new Padding(0, 6, 12, 0)
            };
            _search = new TextBox
            {
                Dock = DockStyle.Fill,
                MinimumSize = new Size(240, 0),
                Name = "RunningExecutableSearch",
                AccessibleName = "搜索运行中 EXE",
                AccessibleDescription = "按可执行文件名筛选，不会修改配置。"
            };
            _search.TextChanged += delegate { ApplyFilter(); };
            searchRow.Controls.Add(searchLabel, 0, 0);
            searchRow.Controls.Add(_search, 1, 0);
            root.Controls.Add(searchRow, 0, 1);

            _list = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false,
                Name = "RunningExecutableList",
                AccessibleName = "正在运行的 EXE 列表",
                AccessibleDescription = "使用空格勾选一个或多个可执行文件。"
            };
            _list.ItemCheck += OnItemCheck;
            root.Controls.Add(_list, 0, 2);

            _status = new Label
            {
                AutoSize = true,
                Name = "RunningExecutableStatus",
                Margin = new Padding(0, 8, 0, 8),
                AccessibleDescription = "运行中 EXE 读取状态",
                LiveSetting = AutomationLiveSetting.Polite
            };
            root.Controls.Add(_status, 0, 3);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0)
            };
            _add = new Button
            {
                AutoSize = true,
                Text = "添加所选",
                Enabled = false,
                Tag = "Settings.PrimaryAction",
                Name = "AddSelectedRunningExecutablesButton",
                Margin = new Padding(8, 0, 0, 0),
                AccessibleDescription = "将勾选的 EXE 添加到游戏模式列表"
            };
            _add.Click += OnAdd;
            var cancel = new Button
            {
                AutoSize = true,
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Margin = new Padding(8, 0, 0, 0)
            };
            _refresh = new Button
            {
                AutoSize = true,
                Text = "刷新列表",
                Name = "RefreshRunningExecutablesButton",
                Margin = new Padding(8, 0, 0, 0),
                AccessibleName = "重新读取运行中 EXE"
            };
            _refresh.Click += OnRefresh;
            actions.Controls.Add(_add);
            actions.Controls.Add(cancel);
            actions.Controls.Add(_refresh);
            root.Controls.Add(actions, 0, 4);

            AcceptButton = _add;
            CancelButton = cancel;
            Shown += OnShown;
            ResumeLayout(true);
        }

        internal bool IsLoading
        {
            get { return _loading; }
        }

        internal IReadOnlyList<string> SelectedExecutableNames
        {
            get
            {
                var result = new List<string>(_selected);
                result.Sort(StringComparer.OrdinalIgnoreCase);
                return result;
            }
        }

        internal static IReadOnlyList<string> FilterExecutableNames(
            IEnumerable<string> names,
            string searchText)
        {
            string search = (searchText ?? "").Trim();
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (names != null)
            {
                foreach (string name in names)
                {
                    string normalized = GameProcessDetector.NormalizeProcessName(name);
                    if (normalized.Length == 0 || !seen.Add(normalized)) continue;
                    string display = FormatExecutableName(normalized);
                    if (search.Length == 0
                        || display.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        result.Add(normalized);
                    }
                }
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                Shown -= OnShown;
                _refresh.Click -= OnRefresh;
                _dialogIcon.Dispose();
                _dialogFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private void LoadCandidatesAsync()
        {
            if (_loading || _disposed || IsDisposed || Disposing) return;
            _loading = true;
            _refresh.Enabled = false;
            _list.Enabled = false;
            _add.Enabled = false;
            _status.Text = "正在读取运行中的 EXE…";
            _status.AccessibleName = _status.Text;
            _status.LiveSetting = AutomationLiveSetting.Polite;
            try
            {
                Task.Run(_loadExecutables).ContinueWith(
                    delegate(Task<IReadOnlyList<string>> task)
                    {
                        PostLoadCompletion(task);
                    },
                    TaskScheduler.Default);
            }
            catch (Exception error)
            {
                _loading = false;
                CompleteLoadFailure(error);
            }
        }

        private void PostLoadCompletion(Task<IReadOnlyList<string>> task)
        {
            if (_disposed || IsDisposed || Disposing)
            {
                _loading = false;
                return;
            }

            try
            {
                BeginInvoke(new Action(delegate { CompleteLoad(task); }));
            }
            catch (ObjectDisposedException)
            {
                _loading = false;
            }
            catch (InvalidOperationException)
            {
                _loading = false;
            }
        }

        private void CompleteLoad(Task<IReadOnlyList<string>> task)
        {
            if (_disposed || IsDisposed || Disposing)
            {
                _loading = false;
                return;
            }

            if (task.IsFaulted)
            {
                Exception error = task.Exception == null
                    ? new InvalidOperationException("未知进程枚举错误。")
                    : task.Exception.GetBaseException();
                CompleteLoadFailure(error);
                return;
            }
            if (task.IsCanceled)
            {
                CompleteLoadFailure(new OperationCanceledException("操作已取消。"));
                return;
            }

            _candidates.Clear();
            var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<string> running = task.Result;
            if (running != null)
            {
                foreach (string value in running)
                {
                    string normalized = GameProcessDetector.NormalizeProcessName(value);
                    if (normalized.Length == 0 || _excluded.Contains(normalized) || !available.Add(normalized))
                    {
                        continue;
                    }
                    _candidates.Add(normalized);
                }
            }
            _candidates.Sort(StringComparer.OrdinalIgnoreCase);
            _selected.RemoveWhere(delegate(string value) { return !available.Contains(value); });
            _status.Text = _candidates.Count == 0
                ? "当前没有可添加的运行中 EXE。"
                : "找到 " + _candidates.Count + " 个可添加的运行中 EXE。";
            _status.AccessibleName = _status.Text;
            FinishLoad();
        }

        private void CompleteLoadFailure(Exception error)
        {
            if (_disposed || IsDisposed || Disposing) return;
            _candidates.Clear();
            _selected.Clear();
            _status.Text = "读取运行中 EXE 失败：" + error.GetType().Name + " - " + error.Message;
            _status.AccessibleName = _status.Text;
            _status.LiveSetting = AutomationLiveSetting.Assertive;
            FinishLoad();
        }

        private void FinishLoad()
        {
            ApplyFilter();
            _loading = false;
            _refresh.Enabled = true;
            _list.Enabled = true;
            UpdateAddButton();
        }

        private void OnShown(object sender, EventArgs e)
        {
            ApplyOwnerTheme();
            LoadCandidatesAsync();
            _search.Focus();
        }

        private void OnRefresh(object sender, EventArgs e)
        {
            LoadCandidatesAsync();
        }

        private void ApplyFilter()
        {
            IReadOnlyList<string> filtered = FilterExecutableNames(_candidates, _search.Text);
            _updatingList = true;
            try
            {
                _list.BeginUpdate();
                _list.Items.Clear();
                foreach (string normalized in filtered)
                {
                    int index = _list.Items.Add(FormatExecutableName(normalized));
                    if (_selected.Contains(normalized)) _list.SetItemChecked(index, true);
                }
            }
            finally
            {
                _list.EndUpdate();
                _updatingList = false;
            }
            UpdateAddButton();
        }

        private void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_updatingList || e.Index < 0 || e.Index >= _list.Items.Count) return;
            string normalized = GameProcessDetector.NormalizeProcessName(_list.Items[e.Index].ToString());
            if (e.NewValue == CheckState.Checked)
            {
                _selected.Add(normalized);
            }
            else
            {
                _selected.Remove(normalized);
            }
            UpdateAddButton();
        }

        private void UpdateAddButton()
        {
            _add.Enabled = _selected.Count > 0;
            _add.Text = _selected.Count > 0
                ? "添加所选（" + _selected.Count + "）"
                : "添加所选";
            _add.AccessibleName = _add.Text;
        }

        private void OnAdd(object sender, EventArgs e)
        {
            if (_selected.Count == 0) return;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ApplyOwnerTheme()
        {
            SettingsThemeKind kind;
            if (SystemInformation.HighContrast)
            {
                kind = SettingsThemeKind.HighContrast;
            }
            else
            {
                Control owner = Owner;
                Color background = owner == null ? SystemColors.Control : owner.BackColor;
                kind = background.GetBrightness() < 0.45F
                    ? SettingsThemeKind.Dark
                    : SettingsThemeKind.Light;
            }
            SettingsThemeStyler.Apply(this, SettingsThemePalette.Create(kind));
        }

        private static HashSet<string> NormalizeSet(IEnumerable<string> values)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (values == null) return result;
            foreach (string value in values)
            {
                string normalized = GameProcessDetector.NormalizeProcessName(value);
                if (normalized.Length > 0) result.Add(normalized);
            }
            return result;
        }

        internal static string FormatExecutableName(string value)
        {
            string normalized = GameProcessDetector.NormalizeProcessName(value);
            return normalized.Length == 0 ? "" : normalized + ".exe";
        }
    }
}

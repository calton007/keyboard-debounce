using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace KeyboardDebounce
{
    public sealed class SettingsForm : Form
    {
        private readonly AppSettings _settings;
        private readonly LearningState _learning;
        private readonly DebounceEngine _engine;
        private readonly SettingsStore _store;
        private readonly Action _save;
        private readonly Action _saveLearning;
        private readonly Func<string> _getStatus;
        private readonly CheckBox _enabled;
        private readonly CheckBox _startup;
        private readonly CheckBox _silentRun;
        private readonly TrackBar _sensitivity;
        private readonly Label _sensitivityText;
        private readonly NumericUpDown _defaultThreshold;
        private readonly NumericUpDown _longHoldBypass;
        private readonly Label _status;
        private readonly DataGridView _grid;
        private readonly Timer _refreshTimer;
        private string _sortColumnName;
        private bool _sortAscending;
        private bool _refreshing;

        public SettingsForm(AppSettings settings, LearningState learning, DebounceEngine engine, SettingsStore store, Action save, Action saveLearning, Func<string> getStatus)
        {
            _settings = settings;
            _learning = learning;
            _engine = engine;
            _store = store;
            _save = save;
            _saveLearning = saveLearning;
            _getStatus = getStatus;

            Text = "Keyboard Debounce";
            Icon = AppIcon.Load();
            Size = new Size(860, 560);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(760, 460);

            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 4;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var top = new FlowLayoutPanel();
            top.Dock = DockStyle.Fill;
            top.AutoSize = true;
            top.Padding = new Padding(10);
            root.Controls.Add(top, 0, 0);

            _enabled = new CheckBox { Text = "启用防抖", AutoSize = true, Checked = _settings.Enabled };
            _enabled.CheckedChanged += delegate { _settings.Enabled = _enabled.Checked; SaveAndRefresh(); };
            top.Controls.Add(_enabled);

            _startup = new CheckBox { Text = "开机自启", AutoSize = true, Checked = _settings.StartWithWindows };
            _startup.CheckedChanged += delegate { _settings.StartWithWindows = _startup.Checked; SaveAndRefresh(); };
            top.Controls.Add(_startup);

            _silentRun = new CheckBox { Text = "静默运行", AutoSize = true, Checked = _settings.SilentRun };
            _silentRun.CheckedChanged += delegate { _settings.SilentRun = _silentRun.Checked; SaveAndRefresh(); };
            top.Controls.Add(_silentRun);

            top.Controls.Add(new Label { Text = "全局敏感度", AutoSize = true, Padding = new Padding(20, 4, 0, 0) });
            _sensitivity = new TrackBar { Minimum = 50, Maximum = 300, TickFrequency = 25, Value = (int)(_settings.GlobalSensitivity * 100), Width = 150 };
            _sensitivity.ValueChanged += delegate
            {
                _settings.GlobalSensitivity = _sensitivity.Value / 100.0;
                _sensitivityText.Text = _settings.GlobalSensitivity.ToString("0.00");
                SaveAndRefresh();
            };
            top.Controls.Add(_sensitivity);
            _sensitivityText = new Label { AutoSize = true, Padding = new Padding(0, 6, 0, 0), Text = _settings.GlobalSensitivity.ToString("0.00") };
            top.Controls.Add(_sensitivityText);

            top.Controls.Add(new Label { Text = "默认阈值(ms)", AutoSize = true, Padding = new Padding(20, 4, 0, 0) });
            _defaultThreshold = new NumericUpDown { Minimum = 20, Maximum = 250, Value = _settings.DefaultThresholdMs, Width = 70 };
            _defaultThreshold.ValueChanged += delegate { _settings.DefaultThresholdMs = (int)_defaultThreshold.Value; SaveAndRefresh(); };
            top.Controls.Add(_defaultThreshold);

            top.Controls.Add(new Label { Text = "长按放行(ms)", AutoSize = true, Padding = new Padding(20, 4, 0, 0) });
            _longHoldBypass = new NumericUpDown { Minimum = 250, Maximum = 1000, Increment = 10, Value = _settings.LongHoldBypassMs, Width = 80 };
            _longHoldBypass.ValueChanged += delegate { _settings.LongHoldBypassMs = (int)_longHoldBypass.Value; SaveAndRefresh(); };
            top.Controls.Add(_longHoldBypass);

            _status = new Label();
            _status.Dock = DockStyle.Fill;
            _status.AutoSize = true;
            _status.Padding = new Padding(10, 0, 10, 8);
            _status.Text = "最近事件：等待按键事件。";
            root.Controls.Add(_status, 0, 1);

            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = false;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = false;
            _grid.CellValueChanged += OnGridCellValueChanged;
            _grid.CurrentCellDirtyStateChanged += OnGridCurrentCellDirtyStateChanged;
            _grid.ColumnHeaderMouseClick += OnGridColumnHeaderMouseClick;
            AddTextColumn("Vk", "VK");
            AddTextColumn("KeyName", "按键");
            AddTextColumn("Threshold", "阈值(ms)");
            AddTextColumn("Accepted", "放行次数");
            AddTextColumn("Suppressed", "拦截次数");
            AddTextColumn("LastSeen", "最近时间");
            var ignoredColumn = new DataGridViewCheckBoxColumn();
            ignoredColumn.Name = "Ignored";
            ignoredColumn.HeaderText = "忽略";
            ignoredColumn.TrueValue = true;
            ignoredColumn.FalseValue = false;
            ignoredColumn.ReadOnly = false;
            ignoredColumn.SortMode = DataGridViewColumnSortMode.Programmatic;
            _grid.Columns.Add(ignoredColumn);
            root.Controls.Add(_grid, 0, 2);

            var bottom = new FlowLayoutPanel();
            bottom.Dock = DockStyle.Fill;
            bottom.AutoSize = true;
            bottom.Padding = new Padding(10);
            root.Controls.Add(bottom, 0, 3);

            AddButton(bottom, "清空忽略列表", delegate
            {
                if (MessageBox.Show("确认清空所有忽略键？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    _engine.ClearIgnoredKeys();
                    SaveAndRefresh();
                }
            });
            AddButton(bottom, "激进防抖", delegate
            {
                if (MessageBox.Show("确认启用激进防抖？这会修改默认阈值、长按放行、全局敏感度，并把已有按键学习阈值改为激进配置。", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    _settings.DefaultThresholdMs = 160;
                    _settings.LongHoldBypassMs = 650;
                    _settings.GlobalSensitivity = 1.0;
                    foreach (var pair in _learning.Keys)
                    {
                        if (!_engine.IsIgnored(pair.Key))
                        {
                            pair.Value.ThresholdMs = 160;
                        }
                    }
                    SaveAllAndRefresh();
                }
            });
            AddButton(bottom, "刷新", delegate { RefreshData(); });
            AddButton(bottom, "重置学习数据", delegate
            {
                if (MessageBox.Show("确认重置所有按键学习数据？", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    _engine.ResetLearning();
                    SaveLearningAndRefresh();
                }
            });
            AddButton(bottom, "管理员权限提示", delegate
            {
                ShowAdminStatus();
            });
            AddButton(bottom, "查看配置文件", delegate
            {
                OpenConfigDirectory();
            });

            _sortColumnName = "Vk";
            _sortAscending = true;
            RefreshData();

            _refreshTimer = new Timer();
            _refreshTimer.Interval = 500;
            _refreshTimer.Tick += delegate { RefreshVisibleValuesOnly(); };
            _refreshTimer.Start();
        }

        public void RefreshData()
        {
            int selectedVk = GetSelectedVk();

            _settings.Normalize();
            _enabled.Checked = _settings.Enabled;
            _startup.Checked = _settings.StartWithWindows;
            _silentRun.Checked = _settings.SilentRun;
            _sensitivity.Value = (int)(_settings.GlobalSensitivity * 100);
            _sensitivityText.Text = _settings.GlobalSensitivity.ToString("0.00");
            _defaultThreshold.Value = _settings.DefaultThresholdMs;
            _longHoldBypass.Value = _settings.LongHoldBypassMs;
            _status.Text = "最近事件：" + (_getStatus == null ? "" : _getStatus());

            var rowsByVk = new Dictionary<int, KeyLearningState>(_learning.Keys);
            if (_settings.IgnoredKeys != null)
            {
                foreach (int ignoredVk in _settings.IgnoredKeys)
                {
                    if (!rowsByVk.ContainsKey(ignoredVk))
                    {
                        rowsByVk[ignoredVk] = null;
                    }
                }
            }

            var sortedRows = BuildRows(rowsByVk);
            try
            {
                SettingsGridRowSorter.Sort(sortedRows, _sortColumnName, _sortAscending);
            }
            catch
            {
                _sortColumnName = "Vk";
                _sortAscending = true;
                SettingsGridRowSorter.Sort(sortedRows, _sortColumnName, _sortAscending);
            }

            _refreshing = true;
            _grid.Rows.Clear();
            foreach (SettingsGridRow row in sortedRows)
            {
                int rowIndex = _grid.Rows.Add(
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    "",
                    false);
                _grid.Rows[rowIndex].Tag = row;
                WriteRowValues(_grid.Rows[rowIndex], row);
                if (row.Vk == selectedVk)
                {
                    _grid.Rows[rowIndex].Selected = true;
                }
            }
            _refreshing = false;
            ApplySortGlyph();
        }

        public void RefreshVisibleValuesOnly()
        {
            _settings.Normalize();
            _status.Text = "最近事件：" + (_getStatus == null ? "" : _getStatus());

            _refreshing = true;
            foreach (DataGridViewRow gridRow in _grid.Rows)
            {
                var existing = gridRow.Tag as SettingsGridRow;
                if (existing == null) continue;

                KeyLearningState state = null;
                _learning.Keys.TryGetValue(existing.Vk, out state);

                var row = BuildRow(existing.Vk, state);
                gridRow.Tag = row;
                WriteRowValues(gridRow, row);
            }
            _refreshing = false;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_refreshTimer != null)
            {
                _refreshTimer.Stop();
                _refreshTimer.Dispose();
            }
            base.OnFormClosed(e);
        }

        private void OnGridColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            string columnName = _grid.Columns[e.ColumnIndex].Name;
            if (_sortColumnName == columnName)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sortColumnName = columnName;
                _sortAscending = true;
            }
            RefreshData();
        }

        private void OnGridCurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell != null && _grid.CurrentCell.OwningColumn.Name == "Ignored")
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void OnGridCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_refreshing || e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (_grid.Columns[e.ColumnIndex].Name != "Ignored") return;

            var row = _grid.Rows[e.RowIndex].Tag as SettingsGridRow;
            if (row == null) return;

            bool ignored = false;
            object value = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;
            if (value is bool)
            {
                ignored = (bool)value;
            }

            if (ignored)
            {
                _engine.IgnoreKey(row.Vk);
            }
            else
            {
                _engine.UnignoreKey(row.Vk);
            }

            if (_save != null) _save();
            if (_saveLearning != null) _saveLearning();
            RefreshData();
        }

        private void SaveAndRefresh()
        {
            if (_save != null) _save();
            RefreshData();
        }

        private void SaveLearningAndRefresh()
        {
            if (_saveLearning != null) _saveLearning();
            RefreshData();
        }

        private void SaveAllAndRefresh()
        {
            if (_save != null) _save();
            if (_saveLearning != null) _saveLearning();
            RefreshData();
        }

        private static void AddButton(Control parent, string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += delegate { action(); };
            parent.Controls.Add(button);
        }

        private void OpenConfigDirectory()
        {
            string dir = Path.GetDirectoryName(_store.SettingsPath);
            if (String.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                MessageBox.Show("配置目录不存在：" + dir, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }

        private void ShowAdminStatus()
        {
            bool isAdmin = NativeMethods.IsUserAnAdmin();
            MessageBox.Show(
                isAdmin ? "当前已经以管理员权限运行。" : "当前为普通权限。管理员权限窗口可能无法被完整覆盖；如确实需要，请手动以管理员身份运行本程序。",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private static string KeyName(int vk)
        {
            try
            {
                return ((Keys)vk).ToString();
            }
            catch
            {
                return "VK " + vk;
            }
        }

        private void AddTextColumn(string name, string headerText)
        {
            var column = new DataGridViewTextBoxColumn();
            column.Name = name;
            column.HeaderText = headerText;
            column.ReadOnly = true;
            column.SortMode = DataGridViewColumnSortMode.Programmatic;
            _grid.Columns.Add(column);
        }

        private int GetSelectedVk()
        {
            if (_grid.SelectedRows.Count == 0) return -1;
            var row = _grid.SelectedRows[0].Tag as SettingsGridRow;
            return row == null ? -1 : row.Vk;
        }

        private List<SettingsGridRow> BuildRows(Dictionary<int, KeyLearningState> rowsByVk)
        {
            var rows = new List<SettingsGridRow>();
            foreach (var pair in rowsByVk)
            {
                rows.Add(BuildRow(pair.Key, pair.Value));
            }
            return rows;
        }

        private SettingsGridRow BuildRow(int vk, KeyLearningState state)
        {
            return new SettingsGridRow
            {
                Vk = vk,
                KeyName = KeyName(vk),
                Ignored = _engine.IsIgnored(vk),
                HasLearning = state != null,
                ThresholdMs = state == null ? 0 : GetEffectiveThresholdForDisplay(state),
                AcceptedCount = state == null ? 0 : state.AcceptedCount,
                SuppressedCount = state == null ? 0 : state.SuppressedCount,
                LastSeen = state == null || state.LastSeenUtc == DateTime.MinValue ? "" : state.LastSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        private void WriteRowValues(DataGridViewRow gridRow, SettingsGridRow row)
        {
            gridRow.Cells["Vk"].Value = row.Vk.ToString("000");
            gridRow.Cells["KeyName"].Value = row.KeyName;
            gridRow.Cells["Threshold"].Value = row.HasLearning ? row.ThresholdMs.ToString() : "-";
            gridRow.Cells["Accepted"].Value = row.HasLearning ? row.AcceptedCount.ToString() : "-";
            gridRow.Cells["Suppressed"].Value = row.HasLearning ? row.SuppressedCount.ToString() : "-";
            gridRow.Cells["LastSeen"].Value = row.LastSeen;
            gridRow.Cells["Ignored"].Value = row.Ignored;
        }

        private int GetEffectiveThresholdForDisplay(KeyLearningState state)
        {
            int value = (int)Math.Round(state.ThresholdMs * _settings.GlobalSensitivity);
            if (value < 20) return 20;
            if (value > 250) return 250;
            return value;
        }

        private void ApplySortGlyph()
        {
            foreach (DataGridViewColumn column in _grid.Columns)
            {
                column.HeaderCell.SortGlyphDirection = SortOrder.None;
            }

            if (_grid.Columns.Contains(_sortColumnName))
            {
                _grid.Columns[_sortColumnName].HeaderCell.SortGlyphDirection = _sortAscending ? SortOrder.Ascending : SortOrder.Descending;
            }
        }
    }
}

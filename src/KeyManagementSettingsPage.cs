using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace KeyboardDebounce
{
    internal sealed class KeyManagementSettingsPage : SettingsPageBase
    {
        private const int MinimumGridViewportHeight = 132;

        private readonly TextBox _searchText;
        private readonly ComboBox _filterScope;
        private readonly NumericUpDown _manualVk;
        private readonly DataGridView _grid;
        private readonly Timer _searchDebounceTimer;
        private readonly TableLayoutPanel _root;
        private readonly Control _filterCardShell;
        private readonly Control _tableCardShell;
        private string _appliedSearchText;
        private string _sortColumnName;
        private bool _sortAscending;

        public KeyManagementSettingsPage(SettingsUiContext context)
            : base(context)
        {
            _sortColumnName = "Vk";
            _sortAscending = true;
            _appliedSearchText = "";

            _root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(24),
                Margin = new Padding(0)
            };
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Controls.Add(_root);

            var filterCard = CreateCard("搜索与筛选", "支持按 VK 和按键名搜索，保留排序和选中状态");
            var filterLayout = CreateTwoColumnForm();
            filterCard.Content.Controls.Add(filterLayout);
            _searchText = new TextBox { Name = "KeySearchTextBox" };
            PreserveEditorWidth(_searchText, 220);
            _searchText.TextChanged += OnSearchTextChanged;
            AddFormRow(filterLayout, "搜索", _searchText);
            _filterScope = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Name = "KeyFilterScope"
            };
            PreserveEditorWidth(_filterScope, 160);
            _filterScope.Items.Add("全部");
            _filterScope.Items.Add("仅已学习");
            _filterScope.Items.Add("仅游戏防抖");
            _filterScope.Items.Add("仅始终忽略");
            _filterScope.SelectedIndexChanged += OnFilterScopeChanged;
            AddFormRow(filterLayout, "范围", _filterScope);

            _manualVk = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 255,
                Value = 65,
                Name = "ManualVirtualKey"
            };
            PreserveEditorWidth(_manualVk, 90);
            AddFormRow(filterLayout, "手动添加 VK", _manualVk);
            var addButtonRow = CreateButtonRow();
            addButtonRow.Controls.Add(CreateButton("添加到游戏防抖", OnAddGameFilteredKey));
            addButtonRow.Controls.Add(CreateButton("刷新按键列表", OnRefreshKeyList));
            filterCard.Content.Controls.Add(addButtonRow);
            _filterCardShell = filterCard.Shell;
            _filterCardShell.Dock = DockStyle.Top;
            _filterCardShell.Margin = new Padding(0, 0, 0, 16);
            _root.Controls.Add(_filterCardShell, 0, 0);

            var tableCard = CreateCard("按键表格", "可直接勾选游戏防抖和始终忽略");
            var tableActions = CreateButtonRow();
            tableActions.WrapContents = false;
            tableActions.Controls.Add(CreateButton("刷新按键列表", OnRefreshKeyList));
            tableActions.Controls.Add(CreateButton("清空忽略列表", OnClearIgnored));
            tableActions.Controls.Add(CreateButton("重置学习数据", OnResetLearning));
            tableCard.Content.Controls.Add(tableActions);
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                Height = MinimumGridViewportHeight,
                MinimumSize = new Size(0, MinimumGridViewportHeight),
                ReadOnly = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                ScrollBars = ScrollBars.Vertical,
                Name = "KeyGrid",
                AccessibleName = "按键管理表格",
                AccessibleDescription = "使用方向键浏览，定位到复选框列后按空格切换。"
            };
            _grid.RowTemplate.Height = 44;
            _grid.CellValueChanged += OnGridCellValueChanged;
            _grid.CurrentCellDirtyStateChanged += OnGridCurrentCellDirtyStateChanged;
            _grid.ColumnHeaderMouseClick += OnGridColumnHeaderMouseClick;
            AddTextColumn("Vk", "VK");
            AddTextColumn("KeyName", "按键");
            AddTextColumn("Threshold", "普通阈值(ms)");
            AddTextColumn("Accepted", "放行次数");
            AddTextColumn("Suppressed", "拦截次数");
            AddTextColumn("LastSeen", "最近时间");
            AddCheckColumn("GameFiltered", "游戏防抖");
            AddCheckColumn("Ignored", "始终忽略");
            tableCard.Content.Controls.Add(_grid);
            _tableCardShell = tableCard.Shell;
            _tableCardShell.Dock = DockStyle.Fill;
            _tableCardShell.Margin = new Padding(0);
            _root.Controls.Add(_tableCardShell, 0, 1);

            _searchDebounceTimer = new Timer { Interval = 150 };
            _searchDebounceTimer.Tick += OnSearchDebounceTimerTick;
            Resize += OnPageResize;

            _filterScope.SelectedIndex = 0;
            RefreshData();
        }

        internal override SettingsPageId PageId
        {
            get { return SettingsPageId.KeyManagement; }
        }

        internal override string PageTitle
        {
            get { return "按键管理"; }
        }

        internal override Control InitialFocusControl
        {
            get { return _searchText; }
        }

        internal TextBox SearchTextBox
        {
            get { return _searchText; }
        }

        internal ComboBox FilterScopeControl
        {
            get { return _filterScope; }
        }

        internal DataGridView Grid
        {
            get { return _grid; }
        }

        internal KeyFilterScope FilterScope
        {
            get { return GetSelectedScope(); }
            set
            {
                switch (value)
                {
                    case KeyFilterScope.LearnedOnly:
                        _filterScope.SelectedIndex = 1;
                        break;
                    case KeyFilterScope.GameFilteredOnly:
                        _filterScope.SelectedIndex = 2;
                        break;
                    case KeyFilterScope.IgnoredOnly:
                        _filterScope.SelectedIndex = 3;
                        break;
                    default:
                        _filterScope.SelectedIndex = 0;
                        break;
                }
            }
        }

        internal string CurrentSortColumn
        {
            get { return _sortColumnName; }
        }

        internal bool SortAscending
        {
            get { return _sortAscending; }
        }

        internal int SearchDebounceInterval
        {
            get { return _searchDebounceTimer.Interval; }
        }

        internal int SelectedVirtualKey
        {
            get { return GetSelectedVk(); }
        }

        internal List<int> GetDisplayedVirtualKeys()
        {
            var values = new List<int>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var model = row.Tag as SettingsGridRow;
                if (model != null) values.Add(model.Vk);
            }
            return values;
        }

        internal void ApplySearchImmediately()
        {
            _searchDebounceTimer.Stop();
            _appliedSearchText = SettingsFilter.NormalizeSearch(_searchText.Text);
            RefreshGrid(true);
        }

        internal void SortByColumn(string columnName)
        {
            if (!_grid.Columns.Contains(columnName))
            {
                throw new ArgumentException("未知的按键表格列：" + columnName, "columnName");
            }

            if (_sortColumnName == columnName)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sortColumnName = columnName;
                _sortAscending = true;
            }
            RefreshGrid(true);
        }

        internal void SelectVirtualKey(int virtualKeyCode)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var model = row.Tag as SettingsGridRow;
                if (model == null || model.Vk != virtualKeyCode) continue;
                row.Selected = true;
                _grid.CurrentCell = row.Cells[0];
                return;
            }
            throw new ArgumentOutOfRangeException("virtualKeyCode");
        }

        public override void RefreshData()
        {
            RefreshGrid(true);
            UpdateGridViewportHeight();
        }

        public override void RefreshLiveState()
        {
            if (_grid.Rows.Count == 0) return;

            int selectedVk = GetSelectedVk();
            int currentColumn = _grid.CurrentCell == null ? -1 : _grid.CurrentCell.ColumnIndex;
            int firstDisplayed = _grid.FirstDisplayedScrollingRowIndex;

            RunRefresh(delegate
            {
                foreach (DataGridViewRow gridRow in _grid.Rows)
                {
                    var existing = gridRow.Tag as SettingsGridRow;
                    if (existing == null) continue;

                    KeyLearningState state = null;
                    Context.Learning.Keys.TryGetValue(existing.Vk, out state);
                    SettingsGridRow updated = BuildRow(existing.Vk, state);
                    gridRow.Tag = updated;
                    WriteRowValues(gridRow, updated);
                }
            });

            RestoreGridState(selectedVk, currentColumn, firstDisplayed);
            UpdateGridViewportHeight();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _searchDebounceTimer != null)
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Dispose();
            }

            base.Dispose(disposing);
        }

        private void AddTextColumn(string name, string headerText)
        {
            var column = new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = headerText,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.Programmatic
            };
            _grid.Columns.Add(column);
        }

        private void AddCheckColumn(string name, string headerText)
        {
            var column = new DataGridViewCheckBoxColumn
            {
                Name = name,
                HeaderText = headerText,
                TrueValue = true,
                FalseValue = false,
                ReadOnly = false,
                SortMode = DataGridViewColumnSortMode.Programmatic
            };
            _grid.Columns.Add(column);
        }

        private void RefreshGrid(bool preserveFocus)
        {
            int selectedVk = GetSelectedVk();
            int currentColumn = preserveFocus && _grid.CurrentCell != null ? _grid.CurrentCell.ColumnIndex : -1;
            int firstDisplayed = preserveFocus && _grid.Rows.Count > 0 ? _grid.FirstDisplayedScrollingRowIndex : -1;

            Context.Settings.Normalize();

            List<SettingsGridRow> rows = BuildRows();
            try
            {
                SettingsGridRowSorter.Sort(rows, _sortColumnName, _sortAscending);
            }
            catch (ArgumentException)
            {
                _sortColumnName = "Vk";
                _sortAscending = true;
                SettingsGridRowSorter.Sort(rows, _sortColumnName, _sortAscending);
            }

            rows = SettingsFilter.Apply(rows, _appliedSearchText, GetSelectedScope());

            RunRefresh(delegate
            {
                _grid.Rows.Clear();
                foreach (SettingsGridRow row in rows)
                {
                    int rowIndex = _grid.Rows.Add("", "", "", "", "", "", false, false);
                    DataGridViewRow gridRow = _grid.Rows[rowIndex];
                    gridRow.Tag = row;
                    WriteRowValues(gridRow, row);
                    if (row.Vk == selectedVk)
                    {
                        gridRow.Selected = true;
                    }
                }
                ApplySortGlyph();
            });

            RestoreGridState(selectedVk, currentColumn, firstDisplayed);
        }

        private void RestoreGridState(int selectedVk, int currentColumn, int firstDisplayed)
        {
            if (_grid.Rows.Count == 0) return;

            foreach (DataGridViewRow row in _grid.Rows)
            {
                var model = row.Tag as SettingsGridRow;
                if (model != null && model.Vk == selectedVk)
                {
                    row.Selected = true;
                    if (currentColumn >= 0 && currentColumn < _grid.Columns.Count)
                    {
                        _grid.CurrentCell = row.Cells[currentColumn];
                    }
                    break;
                }
            }

            if (firstDisplayed >= 0 && firstDisplayed < _grid.Rows.Count)
            {
                _grid.FirstDisplayedScrollingRowIndex = firstDisplayed;
            }
        }

        private List<SettingsGridRow> BuildRows()
        {
            var rowsByVk = new Dictionary<int, KeyLearningState>(Context.Learning.Keys);
            AddConfiguredRows(rowsByVk, Context.Settings.IgnoredKeys);
            AddConfiguredRows(rowsByVk, Context.Settings.GameModeFilteredKeys);

            var rows = new List<SettingsGridRow>();
            foreach (KeyValuePair<int, KeyLearningState> pair in rowsByVk)
            {
                rows.Add(BuildRow(pair.Key, pair.Value));
            }
            return rows;
        }

        private SettingsGridRow BuildRow(int virtualKeyCode, KeyLearningState state)
        {
            return new SettingsGridRow
            {
                Vk = virtualKeyCode,
                KeyName = KeyName(virtualKeyCode),
                Ignored = Context.Engine.IsIgnored(virtualKeyCode),
                GameFiltered = Context.Settings.GameModeFilteredKeys != null
                    && Context.Settings.GameModeFilteredKeys.Contains(virtualKeyCode),
                HasLearning = state != null,
                ThresholdMs = state == null ? 0 : GetEffectiveThresholdForDisplay(state),
                AcceptedCount = state == null ? 0 : state.AcceptedCount,
                SuppressedCount = state == null ? 0 : state.SuppressedCount,
                LastSeen = state == null || state.LastSeenUtc == DateTime.MinValue
                    ? ""
                    : state.LastSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        private static void AddConfiguredRows(Dictionary<int, KeyLearningState> rowsByVk, IEnumerable<int> configuredKeys)
        {
            if (configuredKeys == null) return;
            foreach (int virtualKeyCode in configuredKeys)
            {
                if (!rowsByVk.ContainsKey(virtualKeyCode))
                {
                    rowsByVk[virtualKeyCode] = null;
                }
            }
        }

        private void WriteRowValues(DataGridViewRow gridRow, SettingsGridRow row)
        {
            gridRow.Cells["Vk"].Value = row.Vk.ToString("000");
            gridRow.Cells["KeyName"].Value = row.KeyName;
            gridRow.Cells["Threshold"].Value = row.HasLearning ? row.ThresholdMs.ToString() : "-";
            gridRow.Cells["Accepted"].Value = row.HasLearning ? row.AcceptedCount.ToString() : "-";
            gridRow.Cells["Suppressed"].Value = row.HasLearning ? row.SuppressedCount.ToString() : "-";
            gridRow.Cells["LastSeen"].Value = row.LastSeen;
            gridRow.Cells["GameFiltered"].Value = row.GameFiltered;
            gridRow.Cells["Ignored"].Value = row.Ignored;
        }

        private void ApplySortGlyph()
        {
            foreach (DataGridViewColumn column in _grid.Columns)
            {
                column.HeaderCell.SortGlyphDirection = SortOrder.None;
            }

            if (_grid.Columns.Contains(_sortColumnName))
            {
                _grid.Columns[_sortColumnName].HeaderCell.SortGlyphDirection =
                    _sortAscending ? SortOrder.Ascending : SortOrder.Descending;
            }
        }

        private int GetSelectedVk()
        {
            if (_grid.SelectedRows.Count == 0) return -1;
            var row = _grid.SelectedRows[0].Tag as SettingsGridRow;
            return row == null ? -1 : row.Vk;
        }

        private KeyFilterScope GetSelectedScope()
        {
            switch (_filterScope.SelectedIndex)
            {
                case 1:
                    return KeyFilterScope.LearnedOnly;
                case 2:
                    return KeyFilterScope.GameFilteredOnly;
                case 3:
                    return KeyFilterScope.IgnoredOnly;
                default:
                    return KeyFilterScope.All;
            }
        }

        private void OnSearchTextChanged(object sender, EventArgs e)
        {
            if (IsRefreshing) return;
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void OnSearchDebounceTimerTick(object sender, EventArgs e)
        {
            ApplySearchImmediately();
        }

        private void OnFilterScopeChanged(object sender, EventArgs e)
        {
            if (IsRefreshing) return;
            RefreshGrid(true);
        }

        private void OnAddGameFilteredKey(object sender, EventArgs e)
        {
            if (Context.Settings.GameModeFilteredKeys == null)
            {
                Context.Settings.GameModeFilteredKeys = new List<int>();
            }

            int virtualKeyCode = (int)_manualVk.Value;
            if (!Context.Settings.GameModeFilteredKeys.Contains(virtualKeyCode))
            {
                Context.Settings.GameModeFilteredKeys.Add(virtualKeyCode);
                Context.Settings.GameModeFilteredKeys.Sort();
            }
            Context.SaveSettingsAndRefresh();
        }

        private void OnRefreshKeyList(object sender, EventArgs e)
        {
            RefreshData();
        }

        private void OnGridColumnHeaderMouseClick(object sender, DataGridViewCellMouseEventArgs e)
        {
            SortByColumn(_grid.Columns[e.ColumnIndex].Name);
        }

        private void OnGridCurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (!_grid.IsCurrentCellDirty || _grid.CurrentCell == null) return;
            string columnName = _grid.CurrentCell.OwningColumn.Name;
            if (columnName == "Ignored" || columnName == "GameFiltered")
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void OnGridCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (IsRefreshing || e.RowIndex < 0 || e.ColumnIndex < 0) return;

            string columnName = _grid.Columns[e.ColumnIndex].Name;
            if (columnName != "Ignored" && columnName != "GameFiltered") return;

            var row = _grid.Rows[e.RowIndex].Tag as SettingsGridRow;
            if (row == null) return;

            bool value = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value is bool
                && (bool)_grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value;

            if (columnName == "Ignored")
            {
                if (value)
                {
                    Context.Engine.IgnoreKey(row.Vk);
                }
                else
                {
                    Context.Engine.UnignoreKey(row.Vk);
                }
                Context.SaveAllAndRefresh();
                return;
            }

            if (Context.Settings.GameModeFilteredKeys == null)
            {
                Context.Settings.GameModeFilteredKeys = new List<int>();
            }

            if (value)
            {
                if (!Context.Settings.GameModeFilteredKeys.Contains(row.Vk))
                {
                    Context.Settings.GameModeFilteredKeys.Add(row.Vk);
                    Context.Settings.GameModeFilteredKeys.Sort();
                }
            }
            else
            {
                Context.Settings.GameModeFilteredKeys.Remove(row.Vk);
            }

            Context.SaveSettingsAndRefresh();
        }

        private void OnClearIgnored(object sender, EventArgs e)
        {
            DialogResult confirm = MessageBox.Show(
                "确认清空所有忽略键？",
                FindForm() == null ? "Keyboard Debounce" : FindForm().Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            Context.Engine.ClearIgnoredKeys();
            Context.SaveSettingsAndRefresh();
        }

        private void OnResetLearning(object sender, EventArgs e)
        {
            DialogResult confirm = MessageBox.Show(
                "确认重置所有按键学习数据？",
                FindForm() == null ? "Keyboard Debounce" : FindForm().Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            Context.Engine.ResetLearning();
            Context.SaveLearningAndRefresh();
        }

        private void OnPageResize(object sender, EventArgs e)
        {
            UpdateGridViewportHeight();
        }

        private void UpdateGridViewportHeight()
        {
            if (_root == null || _grid == null || _tableCardShell == null) return;

            int availableHeight = ClientSize.Height
                - _root.Padding.Top
                - _root.Padding.Bottom
                - _filterCardShell.Height
                - _filterCardShell.Margin.Bottom;
            int targetHeight = Math.Max(MinimumGridViewportHeight, availableHeight - 32);
            if (_grid.Height != targetHeight)
            {
                _grid.Height = targetHeight;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Automation;

namespace KeyboardDebounce
{
    internal sealed class GameModeSettingsPage : SettingsPageBase
    {
        private readonly CheckBox _enabled;
        private readonly Label _statusValue;
        private readonly Label _activeExecutableValue;
        private readonly NumericUpDown _threshold;
        private readonly TrackBar _thresholdSlider;
        private readonly Label _thresholdCurrent;
        private readonly NumericUpDown _longHold;
        private readonly TrackBar _longHoldSlider;
        private readonly Label _longHoldCurrent;
        private readonly DataGridView _executables;
        private readonly Label _executableSummary;
        private readonly Label _runningStateError;
        private readonly Button _browseExecutable;
        private readonly Button _browseRunning;
        private readonly HashSet<string> _runningExecutables;
        private readonly Font _sectionFont;
        private DateTime _nextRunningRefreshUtc;
        private bool _runningRefreshInProgress;
        private bool _runningRefreshPending;
        private bool _hasReliableRunningSnapshot;
        private bool _disposed;

        public GameModeSettingsPage(SettingsUiContext context)
            : base(context)
        {
            _runningExecutables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _sectionFont = new Font("Segoe UI", 11F, FontStyle.Bold, GraphicsUnit.Point);
            BrowseExecutableProvider = SelectExecutableFiles;
            RunningExecutableProvider = SelectRunningExecutables;
            RunningExecutableSnapshotProvider = GameProcessDetector.GetRunningExecutableNames;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(24),
                Margin = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var modeCard = CreateCard("当前状态", null);
            modeCard.Shell.Dock = DockStyle.Fill;
            modeCard.Shell.Margin = new Padding(0, 0, 8, 0);

            _enabled = new CheckBox
            {
                AutoSize = true,
                Text = "自动切换游戏模式",
                Name = "ProcessGameModeToggle",
                AccessibleName = "自动切换游戏模式",
                AccessibleDescription = "任一已添加的 EXE 运行时自动进入游戏模式。",
                Margin = new Padding(0, 0, 0, 8)
            };
            _enabled.CheckedChanged += OnEnabledChanged;
            modeCard.Content.Controls.Add(_enabled);

            _statusValue = new Label
            {
                AutoSize = true,
                MinimumSize = new Size(0, 28),
                Font = _sectionFont,
                TextAlign = ContentAlignment.MiddleLeft,
                Name = "GameModeStatusValue",
                AccessibleDescription = "游戏模式当前状态",
                LiveSetting = AutomationLiveSetting.Polite,
                Margin = new Padding(0, 0, 0, 4)
            };
            modeCard.Content.Controls.Add(_statusValue);

            _activeExecutableValue = new Label
            {
                AutoSize = true,
                Visible = false,
                Tag = "Settings.MutedText",
                AccessibleDescription = "当前运行的游戏 EXE",
                Margin = new Padding(0, 0, 0, 16)
            };
            modeCard.Content.Controls.Add(_activeExecutableValue);

            var parameterHeading = CreateLabel("游戏参数");
            parameterHeading.Font = _sectionFont;
            parameterHeading.AccessibleRole = AccessibleRole.StaticText;
            parameterHeading.Margin = new Padding(0, 4, 0, 8);
            modeCard.Content.Controls.Add(parameterHeading);

            TableLayoutPanel thresholdEditor = CreateParameterEditor(
                "阈值上限",
                "限制游戏模式的最大防抖阈值，数值越小响应越快。",
                20,
                250,
                5,
                "游戏阈值上限",
                out _thresholdSlider,
                out _threshold,
                out _thresholdCurrent);
            _threshold.ValueChanged += OnThresholdChanged;
            _thresholdSlider.ValueChanged += OnThresholdSliderChanged;
            modeCard.Content.Controls.Add(thresholdEditor);

            TableLayoutPanel longHoldEditor = CreateParameterEditor(
                "长按放行",
                "按键持续超过该时间后，后续重复输入直接放行。",
                50,
                1000,
                10,
                "游戏长按放行",
                out _longHoldSlider,
                out _longHold,
                out _longHoldCurrent);
            _longHold.ValueChanged += OnLongHoldChanged;
            _longHoldSlider.ValueChanged += OnLongHoldSliderChanged;
            modeCard.Content.Controls.Add(longHoldEditor);

            var executableCard = CreateCard(
                "游戏 EXE",
                "任一已添加的 EXE 运行时自动进入游戏模式。");
            executableCard.Shell.Dock = DockStyle.Fill;
            executableCard.Shell.Margin = new Padding(8, 0, 0, 0);

            var executableActions = CreateButtonRow();
            executableActions.WrapContents = true;
            _browseExecutable = CreateButton("浏览选择 EXE…", OnBrowseExecutable);
            _browseExecutable.Name = "BrowseGameExecutableButton";
            _browseExecutable.Tag = "Settings.PrimaryAction";
            _browseExecutable.AccessibleDescription = "从磁盘选择一个或多个 EXE，只保存文件名，不启动程序。";
            _browseRunning = CreateButton("从正在运行的程序添加…", OnBrowseRunning);
            _browseRunning.Name = "BrowseRunningExecutableButton";
            _browseRunning.AccessibleDescription = "从当前运行的 EXE 中勾选一个或多个项目。";
            executableActions.Controls.Add(_browseExecutable);
            executableActions.Controls.Add(_browseRunning);
            executableCard.Content.Controls.Add(executableActions);

            _executables = CreateExecutableGrid();
            executableCard.Content.Controls.Add(_executables);

            _executableSummary = new Label
            {
                AutoSize = true,
                Tag = "Settings.MutedText",
                Margin = new Padding(0, 8, 0, 0),
                AccessibleDescription = "已添加 EXE 摘要"
            };
            executableCard.Content.Controls.Add(_executableSummary);

            _runningStateError = new Label
            {
                AutoSize = true,
                Visible = false,
                Tag = "Settings.ErrorText",
                Margin = new Padding(0, 4, 0, 0),
                Name = "GameExecutableRefreshError",
                AccessibleDescription = "游戏 EXE 运行状态读取错误",
                LiveSetting = AutomationLiveSetting.Assertive
            };
            executableCard.Content.Controls.Add(_runningStateError);

            root.Controls.Add(modeCard.Shell, 0, 0);
            root.Controls.Add(executableCard.Shell, 1, 0);

            RefreshData();
        }

        internal override SettingsPageId PageId
        {
            get { return SettingsPageId.GameMode; }
        }

        internal override string PageTitle
        {
            get { return "游戏模式"; }
        }

        internal override Control InitialFocusControl
        {
            get { return _enabled; }
        }

        internal CheckBox ProcessModeToggle
        {
            get { return _enabled; }
        }

        internal DataGridView ExecutableGrid
        {
            get { return _executables; }
        }

        internal Button BrowseExecutableButton
        {
            get { return _browseExecutable; }
        }

        internal Button BrowseRunningExecutableButton
        {
            get { return _browseRunning; }
        }

        internal Func<IEnumerable<string>> BrowseExecutableProvider { get; set; }
        internal Func<IEnumerable<string>> RunningExecutableProvider { get; set; }
        internal Func<IReadOnlyList<string>> RunningExecutableSnapshotProvider { get; set; }

        internal bool IsRunningRefreshInProgress
        {
            get { return _runningRefreshInProgress; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposed = true;
                _sectionFont.Dispose();
            }
            base.Dispose(disposing);
        }

        public override void RefreshData()
        {
            string selected = GetSelectedExecutable();
            RunRefresh(delegate
            {
                Context.Settings.Normalize();
                _enabled.Checked = Context.Settings.ProcessGameModeEnabled;
                _statusValue.Text = Context.GetCurrentGameModeStatus();
                _threshold.Value = Context.Settings.GameModeThresholdMs;
                _thresholdSlider.Value = Context.Settings.GameModeThresholdMs;
                _thresholdCurrent.Text = Context.Settings.GameModeThresholdMs + " ms";
                _longHold.Value = Context.Settings.GameModeLongHoldBypassMs;
                _longHoldSlider.Value = Context.Settings.GameModeLongHoldBypassMs;
                _longHoldCurrent.Text = Context.Settings.GameModeLongHoldBypassMs + " ms";
                SynchronizeExecutableRows(selected);
            });

            if (Visible && FindForm() != null) RequestRunningStateRefresh(false);
            UpdateStatusPresentation();
        }

        public override void RefreshLiveState()
        {
            _statusValue.Text = Context.GetCurrentGameModeStatus();
            RequestRunningStateRefresh(false);
            UpdateStatusPresentation();
        }

        internal int AddExecutableSelections(IEnumerable<string> selections)
        {
            if (selections == null) return 0;

            var normalizedSelections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string selection in selections)
            {
                string normalized = GameProcessDetector.NormalizeProcessName(selection);
                if (normalized.Length > 0) normalizedSelections.Add(normalized);
            }

            if (Context.Settings.GameProcesses == null)
            {
                Context.Settings.GameProcesses = new List<string>();
            }

            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string configured in Context.Settings.GameProcesses)
            {
                string normalized = GameProcessDetector.NormalizeProcessName(configured);
                if (normalized.Length > 0) existing.Add(normalized);
            }

            var additions = new List<string>();
            foreach (string normalized in normalizedSelections)
            {
                if (existing.Add(normalized)) additions.Add(normalized);
            }

            if (additions.Count == 0)
            {
                RefreshData();
                return 0;
            }

            Context.Settings.GameProcesses.AddRange(additions);
            Context.Settings.ProcessGameModeEnabled = true;
            Context.Settings.Normalize();
            Context.SaveSettingsAndRefresh();
            return additions.Count;
        }

        private DataGridView CreateExecutableGrid()
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Top,
                Height = 300,
                MinimumSize = new Size(0, 240),
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                MultiSelect = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                Name = "GameExecutableGrid",
                AccessibleName = "已添加的游戏 EXE",
                AccessibleDescription = "使用方向键浏览；移除列按空格，或按 Delete 删除当前 EXE。",
                Margin = new Padding(0, 12, 0, 0)
            };
            grid.RowTemplate.Height = 36;
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ExecutableName",
                HeaderText = "可执行文件",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                MinimumWidth = 160,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "ExecutableStatus",
                HeaderText = "状态",
                Width = 96,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            grid.Columns.Add(new DataGridViewButtonColumn
            {
                Name = "RemoveExecutable",
                HeaderText = "操作",
                Text = "移除",
                UseColumnTextForButtonValue = true,
                Width = 72,
                FlatStyle = FlatStyle.Flat,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            grid.CellContentClick += OnExecutableCellContentClick;
            grid.KeyDown += OnExecutableGridKeyDown;
            return grid;
        }

        private static TableLayoutPanel CreateParameterEditor(
            string title,
            string description,
            int minimum,
            int maximum,
            int increment,
            string accessibleName,
            out TrackBar slider,
            out NumericUpDown numeric,
            out Label currentValue)
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 4,
                Margin = new Padding(0, 0, 0, 16)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var titleLabel = new Label
            {
                AutoSize = true,
                Text = title,
                Margin = new Padding(0, 6, 12, 0)
            };
            numeric = new NumericUpDown
            {
                Minimum = minimum,
                Maximum = maximum,
                Increment = increment,
                AccessibleName = accessibleName,
                AccessibleDescription = "范围 " + minimum + " 到 " + maximum + " 毫秒。",
                Margin = new Padding(0, 0, 6, 0)
            };
            PreserveEditorWidth(numeric, 90);
            var unit = new Label
            {
                AutoSize = true,
                Text = "ms",
                Margin = new Padding(0, 6, 0, 0)
            };
            var descriptionLabel = new Label
            {
                AutoSize = true,
                Text = description,
                Tag = "Settings.MutedText",
                Margin = new Padding(0, 2, 0, 4)
            };
            panel.Controls.Add(titleLabel, 0, 0);
            panel.Controls.Add(numeric, 1, 0);
            panel.Controls.Add(unit, 2, 0);
            panel.Controls.Add(descriptionLabel, 0, 1);
            panel.SetColumnSpan(descriptionLabel, 3);

            slider = new TrackBar
            {
                Minimum = minimum,
                Maximum = maximum,
                SmallChange = increment,
                LargeChange = Math.Max(increment, (maximum - minimum) / 10),
                TickStyle = TickStyle.None,
                Dock = DockStyle.Fill,
                AccessibleName = accessibleName + "滑块",
                AccessibleDescription = "使用方向键调整，范围 " + minimum + " 到 " + maximum + " 毫秒。",
                Margin = new Padding(0, 0, 0, 0)
            };
            PreserveEditorWidth(slider, 220);
            panel.Controls.Add(slider, 0, 2);
            panel.SetColumnSpan(slider, 3);

            var scale = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                Margin = new Padding(0)
            };
            scale.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            scale.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
            scale.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            var minLabel = new Label
            {
                AutoSize = true,
                Text = minimum + " ms",
                Tag = "Settings.MutedText",
                Anchor = AnchorStyles.Left
            };
            currentValue = new Label
            {
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter,
                Anchor = AnchorStyles.None,
                AccessibleName = accessibleName + "当前值"
            };
            var maxLabel = new Label
            {
                AutoSize = true,
                Text = maximum + " ms",
                Tag = "Settings.MutedText",
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Right
            };
            scale.Controls.Add(minLabel, 0, 0);
            scale.Controls.Add(currentValue, 1, 0);
            scale.Controls.Add(maxLabel, 2, 0);
            panel.Controls.Add(scale, 0, 3);
            panel.SetColumnSpan(scale, 3);
            return panel;
        }

        private void SynchronizeExecutableRows(string selected)
        {
            List<string> configured = Context.Settings.GameProcesses ?? new List<string>();
            bool same = _executables.Rows.Count == configured.Count;
            if (same)
            {
                for (int index = 0; index < configured.Count; index++)
                {
                    string existing = _executables.Rows[index].Tag as string;
                    if (!String.Equals(existing, configured[index], StringComparison.OrdinalIgnoreCase))
                    {
                        same = false;
                        break;
                    }
                }
            }

            if (!same)
            {
                _executables.Rows.Clear();
                foreach (string normalized in configured)
                {
                    int index = _executables.Rows.Add(
                        RunningExecutablePickerDialog.FormatExecutableName(normalized),
                        "未运行",
                        "移除");
                    _executables.Rows[index].Tag = normalized;
                }
            }

            if (!String.IsNullOrEmpty(selected))
            {
                foreach (DataGridViewRow row in _executables.Rows)
                {
                    if (String.Equals(row.Tag as string, selected, StringComparison.OrdinalIgnoreCase))
                    {
                        row.Selected = true;
                        _executables.CurrentCell = row.Cells[0];
                        break;
                    }
                }
            }

            UpdateExecutableSummary();
            UpdateExecutableStatuses();
            UpdateStatusPresentation();
        }

        private void UpdateStatusPresentation()
        {
            const string activePrefix = "游戏模式已开启 · ";
            const string activeSuffix = " 正在运行";
            string authoritativeStatus = Context.GetCurrentGameModeStatus() ?? "";
            string activeExecutable = "";
            bool active = authoritativeStatus.StartsWith(activePrefix, StringComparison.Ordinal)
                && authoritativeStatus.EndsWith(activeSuffix, StringComparison.Ordinal);
            if (active)
            {
                activeExecutable = authoritativeStatus.Substring(
                    activePrefix.Length,
                    authoritativeStatus.Length - activePrefix.Length - activeSuffix.Length).Trim();
                active = activeExecutable.Length > 0;
            }

            if (active)
            {
                _statusValue.Text = "游戏模式已开启";
                _activeExecutableValue.Text = "当前运行的 EXE：" + activeExecutable;
                _activeExecutableValue.Visible = true;
            }
            else
            {
                _statusValue.Text = authoritativeStatus;
                _activeExecutableValue.Text = "";
                _activeExecutableValue.Visible = false;
            }
            _statusValue.AccessibleName = _statusValue.Text;
            _activeExecutableValue.AccessibleName = _activeExecutableValue.Text;

            if (SystemInformation.HighContrast)
            {
                _statusValue.ForeColor = SystemColors.WindowText;
            }
            else
            {
                Color background = _statusValue.Parent == null
                    ? BackColor
                    : _statusValue.Parent.BackColor;
                _statusValue.ForeColor = active
                    ? GetRunningStatusColor(background)
                    : ForeColor;
            }
        }

        private void RequestRunningStateRefresh(bool force)
        {
            if (_disposed || IsDisposed || Disposing || !Visible || FindForm() == null) return;

            if (Context.Settings.GameProcesses == null || Context.Settings.GameProcesses.Count == 0)
            {
                _runningExecutables.Clear();
                _hasReliableRunningSnapshot = true;
                SetRunningRefreshError("");
                UpdateExecutableStatuses();
                return;
            }

            DateTime now = DateTime.UtcNow;
            if (!force && now < _nextRunningRefreshUtc) return;
            if (_runningRefreshInProgress)
            {
                _runningRefreshPending = true;
                return;
            }

            Func<IReadOnlyList<string>> provider = RunningExecutableSnapshotProvider;
            if (provider == null) return;

            _runningRefreshInProgress = true;
            _runningRefreshPending = false;
            _nextRunningRefreshUtc = now.AddSeconds(1);
            try
            {
                Task.Run(provider).ContinueWith(
                    delegate(Task<IReadOnlyList<string>> task)
                    {
                        PostRunningRefreshCompletion(task);
                    },
                    TaskScheduler.Default);
            }
            catch (Exception error)
            {
                _runningRefreshInProgress = false;
                _hasReliableRunningSnapshot = false;
                SetRunningRefreshError(DescribeRunningRefreshError(error));
                UpdateExecutableStatuses();
            }
        }

        private void PostRunningRefreshCompletion(Task<IReadOnlyList<string>> task)
        {
            if (_disposed || IsDisposed || Disposing)
            {
                _runningRefreshInProgress = false;
                return;
            }

            try
            {
                BeginInvoke(new Action(delegate { CompleteRunningStateRefresh(task); }));
            }
            catch (ObjectDisposedException)
            {
                _runningRefreshInProgress = false;
            }
            catch (InvalidOperationException)
            {
                _runningRefreshInProgress = false;
            }
        }

        private void CompleteRunningStateRefresh(Task<IReadOnlyList<string>> task)
        {
            if (_disposed || IsDisposed || Disposing)
            {
                _runningRefreshInProgress = false;
                return;
            }

            if (task.IsFaulted)
            {
                Exception error = task.Exception == null
                    ? new InvalidOperationException("未知进程枚举错误。")
                    : task.Exception.GetBaseException();
                _hasReliableRunningSnapshot = false;
                SetRunningRefreshError(DescribeRunningRefreshError(error));
            }
            else if (task.IsCanceled)
            {
                _hasReliableRunningSnapshot = false;
                SetRunningRefreshError("读取运行状态失败：操作已取消。");
            }
            else
            {
                _runningExecutables.Clear();
                IReadOnlyList<string> running = task.Result;
                if (running != null)
                {
                    foreach (string executable in running)
                    {
                        string normalized = GameProcessDetector.NormalizeProcessName(executable);
                        if (normalized.Length > 0) _runningExecutables.Add(normalized);
                    }
                }
                _hasReliableRunningSnapshot = true;
                SetRunningRefreshError("");
            }

            _runningRefreshInProgress = false;
            UpdateExecutableStatuses();
            bool refreshAgain = _runningRefreshPending;
            _runningRefreshPending = false;
            if (refreshAgain && DateTime.UtcNow >= _nextRunningRefreshUtc)
            {
                RequestRunningStateRefresh(false);
            }
        }

        private static string DescribeRunningRefreshError(Exception error)
        {
            return "读取运行状态失败：" + error.GetType().Name + " - " + error.Message;
        }

        private void UpdateExecutableStatuses()
        {
            foreach (DataGridViewRow row in _executables.Rows)
            {
                string normalized = row.Tag as string;
                bool running = !String.IsNullOrEmpty(normalized)
                    && _runningExecutables.Contains(normalized);
                DataGridViewCell statusCell = row.Cells["ExecutableStatus"];
                statusCell.Value = !_hasReliableRunningSnapshot
                    ? "状态未知"
                    : running ? "正在运行" : "未运行";
                statusCell.Style.ForeColor = _hasReliableRunningSnapshot && running
                    ? SystemInformation.HighContrast
                        ? SystemColors.WindowText
                        : GetRunningStatusColor(_executables.DefaultCellStyle.BackColor)
                    : Color.Empty;
            }
        }

        private void UpdateExecutableSummary()
        {
            int count = Context.Settings.GameProcesses == null
                ? 0
                : Context.Settings.GameProcesses.Count;
            _executableSummary.Text = count == 0
                ? "尚未添加。请选择 EXE，或从正在运行的程序中添加。"
                : "共 " + count + " 个 EXE · 按文件名匹配 · 启动或退出后约 1 秒切换";
            _executableSummary.AccessibleName = _executableSummary.Text;
        }

        private void SetRunningRefreshError(string message)
        {
            _runningStateError.Text = message ?? "";
            _runningStateError.AccessibleName = _runningStateError.Text;
            _runningStateError.Visible = _runningStateError.Text.Length > 0;
        }

        private static Color GetRunningStatusColor(Color background)
        {
            return background.GetBrightness() < 0.45F
                ? Color.FromArgb(0x22, 0xC5, 0x5E)
                : Color.FromArgb(0x15, 0x80, 0x3D);
        }

        private void OnEnabledChanged(object sender, EventArgs e)
        {
            if (IsRefreshing) return;
            Context.Settings.ProcessGameModeEnabled = _enabled.Checked;
            Context.SaveSettingsAndRefresh();
        }

        private void OnThresholdChanged(object sender, EventArgs e)
        {
            if (IsRefreshing) return;
            int value = (int)_threshold.Value;
            _thresholdSlider.Value = value;
            _thresholdCurrent.Text = value + " ms";
            Context.Settings.GameModeThresholdMs = value;
            Context.SaveSettingsAndRefresh();
        }

        private void OnThresholdSliderChanged(object sender, EventArgs e)
        {
            if (IsRefreshing) return;
            _threshold.Value = _thresholdSlider.Value;
        }

        private void OnLongHoldChanged(object sender, EventArgs e)
        {
            if (IsRefreshing) return;
            int value = (int)_longHold.Value;
            _longHoldSlider.Value = value;
            _longHoldCurrent.Text = value + " ms";
            Context.Settings.GameModeLongHoldBypassMs = value;
            Context.SaveSettingsAndRefresh();
        }

        private void OnLongHoldSliderChanged(object sender, EventArgs e)
        {
            if (IsRefreshing) return;
            _longHold.Value = _longHoldSlider.Value;
        }

        private void OnBrowseExecutable(object sender, EventArgs e)
        {
            IEnumerable<string> selected = BrowseExecutableProvider == null
                ? null
                : BrowseExecutableProvider();
            AddExecutableSelections(selected);
        }

        private void OnBrowseRunning(object sender, EventArgs e)
        {
            IEnumerable<string> selected = RunningExecutableProvider == null
                ? null
                : RunningExecutableProvider();
            AddExecutableSelections(selected);
        }

        private IEnumerable<string> SelectExecutableFiles()
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "选择游戏 EXE",
                Filter = "Windows 可执行文件 (*.exe)|*.exe",
                CheckFileExists = true,
                ValidateNames = true,
                Multiselect = true,
                RestoreDirectory = true
            })
            {
                return dialog.ShowDialog(FindForm()) == DialogResult.OK
                    ? dialog.FileNames
                    : Array.Empty<string>();
            }
        }

        private IEnumerable<string> SelectRunningExecutables()
        {
            using (var dialog = new RunningExecutablePickerDialog(
                Context.Settings.GameProcesses,
                GameProcessDetector.GetRunningExecutableNames))
            {
                return dialog.ShowDialog(FindForm()) == DialogResult.OK
                    ? dialog.SelectedExecutableNames
                    : Array.Empty<string>();
            }
        }

        private void OnExecutableCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (!String.Equals(
                _executables.Columns[e.ColumnIndex].Name,
                "RemoveExecutable",
                StringComparison.Ordinal))
            {
                return;
            }
            RemoveExecutable(_executables.Rows[e.RowIndex].Tag as string);
        }

        private void OnExecutableGridKeyDown(object sender, KeyEventArgs e)
        {
            bool removeShortcut = e.KeyCode == Keys.Delete;
            if (!removeShortcut
                && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
                && _executables.CurrentCell != null)
            {
                removeShortcut = String.Equals(
                    _executables.CurrentCell.OwningColumn.Name,
                    "RemoveExecutable",
                    StringComparison.Ordinal);
            }
            if (!removeShortcut) return;
            string selected = GetSelectedExecutable();
            if (String.IsNullOrEmpty(selected)) return;
            RemoveExecutable(selected);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void RemoveExecutable(string normalized)
        {
            if (String.IsNullOrEmpty(normalized) || Context.Settings.GameProcesses == null) return;
            int removed = Context.Settings.GameProcesses.RemoveAll(
                delegate(string value)
                {
                    return String.Equals(value, normalized, StringComparison.OrdinalIgnoreCase);
                });
            if (removed > 0) Context.SaveSettingsAndRefresh();
        }

        private string GetSelectedExecutable()
        {
            return _executables.CurrentRow == null
                ? null
                : _executables.CurrentRow.Tag as string;
        }
    }
}

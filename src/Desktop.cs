using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MosaicToolkit
{
    public class MainForm : Form
    {
        private readonly TextBox exeBox = new TextBox(), keywords = new TextBox(), detail = new TextBox(), activity = new TextBox();
        private readonly Label detection = new Label(), connection = new Label(), help = new Label(), ruleStatus = new Label();
        private readonly DataGridView candidates = Grid(), rules = Grid();
        private readonly DataGridView allRenderers = Grid();
        private readonly TextBox allFilter = new TextBox(), allDetail = new TextBox();
        private readonly Label allCount = new Label();
        private readonly ComboBox allState = Choice(), allType = Choice(), allSort = Choice(), allDirection = Choice();
        private readonly Dictionary<int, long> discoveryOrder = new Dictionary<int, long>();
        private long nextDiscovery;
        private bool changingFilters;
        private readonly List<MaterialDetailsForm> materialWindows = new List<MaterialDetailsForm>();
        private TabControl tabs;
        private TabPage allTab;
        private readonly Timer timer = new Timer();
        private readonly List<Control> actions = new List<Control>();
        private GameInfo game;
        private Profile profile = new Profile();
        private Snapshot snapshot, displayedSnapshot;
        private Snapshot cachedSnapshot;
        private Task<Snapshot> snapshotRead;
        private DateTime snapshotStamp;
        private DateTime syncedProfileStamp;
        private readonly CheckBox autoDiscover = new CheckBox();
        private readonly CheckBox backupChanges = new CheckBox();
        private bool syncingAuto;
        private string pending, lastSession, lastMessage, lastReadError, lastConnectionReason;
        private DateTime pendingSince;
        private bool busy, updatingGrid;
        private readonly Color blue = Color.FromArgb(39, 107, 165);

        public MainForm()
        {
            Text = "Mosaic Toolkit 0.1.10 · Unity 遮罩检查工具";
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(244, 247, 250); ForeColor = Color.FromArgb(32, 49, 66);
            ClientSize = new Size(1180, 790); MinimumSize = new Size(950, 690);
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 7 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 63));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            Controls.Add(root);
            var title = new Label { Text = "Mosaic Toolkit", Font = new Font("Microsoft YaHei UI", 17, FontStyle.Bold), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            root.Controls.Add(title, 0, 0);
            var choose = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
            choose.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); choose.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155)); choose.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
            exeBox.Dock = DockStyle.Fill; exeBox.Margin = new Padding(0, 6, 8, 0);
            choose.Controls.Add(exeBox, 0, 0);
            choose.Margin = Padding.Empty;
            choose.Controls.Add(FillButton("选择游戏 EXE", delegate { SelectGame(); }), 1, 0);
            choose.Controls.Add(FillButton("检测", delegate { DetectGame(); }), 2, 0);
            root.Controls.Add(choose, 0, 1);
            detection.Dock = DockStyle.Fill; detection.Text = "选择真正的游戏 EXE。第一版支持 BepInEx 5 / Mono；IL2CPP 仅识别。";
            detection.Padding = new Padding(2, 4, 0, 0); root.Controls.Add(detection, 0, 2);
            var operations = Flow();
            operations.Controls.Add(Button("安装扫描插件", async delegate { try { await InstallScanner(); } catch (Exception e) { Error(e); } }, true));
            operations.Controls.Add(Button("生成并启用默认规则", delegate { GenerateDefaults(); }, true));
            operations.Controls.Add(Button("启动游戏", delegate { Launch(); }));
            operations.Controls.Add(Button("打开游戏日志", delegate { OpenLog(); }));
            operations.Controls.Add(Button("卸载本工具插件", delegate { Uninstall(); }));
            operations.Controls.Add(Button("BepInEx 下载", delegate { Open("https://github.com/BepInEx/BepInEx/releases"); }));
            root.Controls.Add(operations, 0, 3);
            connection.Text = "尚未连接游戏"; connection.Dock = DockStyle.Fill; connection.TextAlign = ContentAlignment.MiddleLeft;
            connection.BackColor = Color.FromArgb(226, 234, 242); connection.Padding = new Padding(8, 0, 0, 0);
            root.Controls.Add(connection, 0, 4);

            tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(15, 6) };
            var scanTab = new TabPage("候选对象"); var ruleTab = new TabPage("保存的规则"); var logTab = new TabPage("操作记录");
            allTab = new TabPage("全部 Renderer");
            tabs.TabPages.AddRange(new[] { scanTab, allTab, ruleTab, logTab }); root.Controls.Add(tabs, 0, 5);
            tabs.SelectedIndexChanged += delegate { if (displayedSnapshot != null) UpdateCandidates(displayedSnapshot); ShowDetail(); };
            var scan = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8) };
            scan.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); scan.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
            scan.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); scan.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); scan.RowStyles.Add(new RowStyle(SizeType.Absolute, 85)); scanTab.Controls.Add(scan);
            var search = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
            search.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66)); search.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); search.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130)); search.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            search.Controls.Add(new Label { Text = "关键词", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            keywords.Text = "mosaic, モザ, censor"; keywords.Dock = DockStyle.Fill; keywords.Margin = new Padding(0, 6, 8, 0); search.Controls.Add(keywords, 1, 0);
            search.Margin = Padding.Empty;
            search.Controls.Add(FillButton("应用", delegate { ApplyKeywords(); }), 2, 0);
            search.Controls.Add(FillButton("重新扫描", delegate { Send("scan", new int[0]); }), 3, 0); scan.Controls.Add(search, 0, 0);
            help.Text = "首次使用：安装扫描插件 → 启动游戏 → 进入目标场景。发现候选不代表已确认；可按 Ctrl / Shift 多选。";
            help.Dock = DockStyle.Fill; help.ForeColor = Color.FromArgb(96, 116, 135); scan.Controls.Add(help, 0, 1);
            AddColumn(candidates, "name", "对象", 150); AddColumn(candidates, "renderer", "组件", 155); AddColumn(candidates, "shader", "Shader", 210);
            AddColumn(candidates, "state", "状态", 105); AddColumn(candidates, "evidence", "命中依据", 115); AddColumn(candidates, "path", "完整路径", 260);
            candidates.SelectionChanged += delegate { ShowDetail(); }; scan.Controls.Add(candidates, 0, 2);
            candidates.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) OpenMaterialDetails(); };
            var temp = Flow(); temp.Controls.Add(Button("临时关闭选中", delegate { SendSelected("off"); }, true));
            temp.Controls.Add(Button("恢复选中", delegate { SendSelected("restore"); }));
            temp.Controls.Add(Button("选中项加入规则", delegate { AddRules(); }));
            temp.Controls.Add(Button("材质详情", delegate { OpenMaterialDetails(); }));
            temp.Controls.Add(Button("停止全部并恢复", delegate { StopAll(); })); scan.Controls.Add(temp, 0, 3);
            detail.Multiline = true; detail.ReadOnly = true; detail.Dock = DockStyle.Fill; detail.ScrollBars = ScrollBars.Vertical;
            detail.BackColor = Color.White; detail.Text = "选中候选后显示组件、材质、路径和关闭状态。"; scan.Controls.Add(detail, 0, 4);

            var allLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(8) };
            allLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); allLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); allLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
            allLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); allLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); allLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 85));
            allTab.Controls.Add(allLayout);
            var filterRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
            filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66)); filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            filterRow.Controls.Add(new Label { Text = "筛选", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            allFilter.Dock = DockStyle.Fill; allFilter.Margin = new Padding(0, 6, 8, 0);
            allFilter.TextChanged += delegate { RefreshAllView(); };
            filterRow.Controls.Add(allFilter, 1, 0); filterRow.Controls.Add(FillButton("重新扫描", delegate { Send("scan", new int[0]); }), 2, 0);
            allLayout.Controls.Add(filterRow, 0, 0);
            var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, Margin = Padding.Empty };
            foreach (float width in new[] { 44f, 180f, 44f, 170f, 44f, 140f, 80f, 90f }) options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
            allState.Items.AddRange(new object[] { "全部状态", "已启用", "已关闭", "持续关闭", "隐藏材质槽", "对象未激活", "规则命中", "规则未命中" });
            allType.Items.Add("全部组件");
            allSort.Items.AddRange(new object[] { "发现顺序", "状态", "对象名", "组件", "Shader", "命中依据", "路径" });
            allDirection.Items.AddRange(new object[] { "升序", "降序" });
            foreach (ComboBox choice in new[] { allState, allType, allSort, allDirection }) { choice.SelectedIndex = 0; choice.SelectedIndexChanged += delegate { RefreshAllView(); }; }
            options.Controls.Add(new Label { Text = "状态", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0); options.Controls.Add(allState, 1, 0);
            options.Controls.Add(new Label { Text = "组件", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 0); options.Controls.Add(allType, 3, 0);
            options.Controls.Add(new Label { Text = "排序", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 4, 0); options.Controls.Add(allSort, 5, 0); options.Controls.Add(allDirection, 6, 0);
            options.Controls.Add(FillButton("重置", delegate { ResetAllFilters(); }), 7, 0); allLayout.Controls.Add(options, 0, 1);
            allCount.Text = "显示当前已加载场景中的全部 Renderer，包含未激活对象；可按名称、组件、Shader、材质或路径筛选。";
            allCount.Dock = DockStyle.Fill; allCount.ForeColor = Color.FromArgb(96, 116, 135); allLayout.Controls.Add(allCount, 0, 2);
            AddColumn(allRenderers, "name", "对象", 150); AddColumn(allRenderers, "renderer", "组件", 155); AddColumn(allRenderers, "shader", "Shader", 210);
            AddColumn(allRenderers, "state", "状态", 105); AddColumn(allRenderers, "evidence", "命中依据", 115); AddColumn(allRenderers, "path", "完整路径", 260);
            foreach (DataGridViewColumn column in allRenderers.Columns) column.SortMode = DataGridViewColumnSortMode.Programmatic;
            allRenderers.ColumnHeaderMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e) { if (e.ColumnIndex >= 0) SortAllColumn(e.ColumnIndex); };
            allRenderers.SelectionChanged += delegate { ShowDetail(); }; allLayout.Controls.Add(allRenderers, 0, 3);
            allRenderers.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) OpenMaterialDetails(); };
            var allButtons = Flow(); allButtons.Controls.Add(Button("临时关闭选中", delegate { SendSelected("off"); }, true));
            allButtons.Controls.Add(Button("恢复选中", delegate { SendSelected("restore"); })); allButtons.Controls.Add(Button("选中项加入规则", delegate { AddRules(); }));
            allButtons.Controls.Add(Button("材质详情", delegate { OpenMaterialDetails(); }));
            allButtons.Controls.Add(Button("停止全部并恢复", delegate { StopAll(); })); allLayout.Controls.Add(allButtons, 0, 4);
            allDetail.Multiline = true; allDetail.ReadOnly = true; allDetail.Dock = DockStyle.Fill; allDetail.ScrollBars = ScrollBars.Vertical; allDetail.BackColor = Color.White;
            allDetail.Text = "此页包含身体、衣服、场景等普通物体；只对你选中的 Renderer 操作。"; allLayout.Controls.Add(allDetail, 0, 5);

            var ruleLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(8) };
            ruleLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110)); ruleLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); ruleLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43)); ruleTab.Controls.Add(ruleLayout);
            ruleStatus.Dock = DockStyle.Fill; ruleStatus.Text = "规则使用准确对象名 + 组件类型 + Shader 匹配。加入规则不会自动启用。";
            var ruleHeader = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            ruleHeader.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); ruleHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            ruleHeader.Controls.Add(ruleStatus, 0, 0);
            autoDiscover.Text = "自动补充新场景规则（关闭工具后也继续）"; autoDiscover.AutoSize = true;
            autoDiscover.CheckedChanged += delegate { if (!syncingAuto) { try { SetAutoDiscovery(); } catch (Exception e) { RefreshRules(); Error(e); } } };
            backupChanges.Text = "操作前备份（默认关闭）"; backupChanges.AutoSize = true;
            backupChanges.CheckedChanged += delegate { if (!syncingAuto) { try { SetBackupPreference(); } catch (Exception e) { RefreshRules(); Error(e); } } };
            var preferences = Flow(); preferences.Controls.Add(autoDiscover); preferences.Controls.Add(backupChanges);
            ruleHeader.Controls.Add(preferences, 0, 1); ruleLayout.Controls.Add(ruleHeader, 0, 0);
            AddColumn(rules, "name", "准确对象名", 150); AddColumn(rules, "type", "组件类型", 155); AddColumn(rules, "shader", "Shader / 材质槽条件", 340); AddColumn(rules, "path", "路径限制（空=不限）", 180);
            AddColumn(rules, "mode", "动作", 140); AddColumn(rules, "enabled", "状态", 75);
            rules.Columns["mode"].DisplayIndex = 1; rules.Columns["enabled"].DisplayIndex = 2;
            ruleLayout.Controls.Add(rules, 0, 1);
            var ruleButtons = Flow(); ruleButtons.Controls.Add(Button("移除选中规则", delegate { RemoveRules(); }));
            ruleButtons.Controls.Add(Button("启停选中规则", delegate { ToggleRules(); }));
            ruleButtons.Controls.Add(Button("导入规则 JSON", delegate { ImportProfile(); })); ruleButtons.Controls.Add(Button("导出规则 JSON", delegate { ExportProfile(); }));
            ruleLayout.Controls.Add(ruleButtons, 0, 2);
            activity.Multiline = true; activity.ReadOnly = true; activity.ScrollBars = ScrollBars.Both; activity.WordWrap = false; activity.Dock = DockStyle.Fill; activity.BackColor = Color.White;
            logTab.Controls.Add(activity);
            var bottom = Flow(); bottom.Padding = new Padding(0, 7, 0, 0);
            bottom.Controls.Add(Button("启用保存的规则", delegate { EnableRules(); }, true));
            bottom.Controls.Add(Button("停用自动规则", delegate { DisableRules(); }));
            bottom.Controls.Add(Button("导出自动补丁 ZIP", async delegate { try { await ExportPatch(); } catch (Exception e) { Error(e); } }, true));
            bottom.Controls.Add(Button("打开输出文件夹", delegate { NeedGame(); DesktopServices.NoReparse(game.WorkDir); Directory.CreateDirectory(game.WorkDir); Open(game.WorkDir); }));
            root.Controls.Add(bottom, 0, 6);
            timer.Interval = 1000; timer.Tick += delegate { Poll(); }; timer.Start();
            Log("就绪。点击“生成并启用默认规则”可按当前已加载对象初始化；支持停止全部并恢复。");
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { e.Cancel = true; Log("构建尚未结束，请稍候再关闭。"); } };
        }
        private static ComboBox Choice()
        { return new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 5, 8, 0), DropDownWidth = 240 }; }
        private static DataGridView Grid()
        {
            var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = true,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None, BackgroundColor = Color.White, BorderStyle = BorderStyle.None,
                EnableHeadersVisualStyles = false, AllowUserToOrderColumns = true };
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(232, 238, 244);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(32, 49, 66);
            grid.ColumnHeadersHeight = 32; grid.RowTemplate.Height = 29;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(39, 107, 165);
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 250, 252);
            return grid;
        }
        private static void AddColumn(DataGridView g, string name, string text, int width)
        { g.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = text, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable }); }
        private FlowLayoutPanel Flow() { return new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true, Margin = Padding.Empty }; }
        private Button Button(string text, EventHandler handler, bool primary = false)
        {
            var b = new Button { Text = text, AutoSize = true, Height = 32, MinimumSize = new Size(75, 32), Padding = new Padding(8, 0, 8, 0),
                FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 2, 7, 2), BackColor = primary ? blue : Color.White, ForeColor = primary ? Color.White : ForeColor };
            b.FlatAppearance.BorderColor = primary ? blue : Color.FromArgb(189, 201, 214);
            b.Click += delegate(object sender, EventArgs e) { try { handler(sender, e); } catch (Exception x) { Error(x); } };
            actions.Add(b); return b;
        }
        private Button FillButton(string text, EventHandler handler)
        {
            Button b = Button(text, handler);
            b.AutoSize = false; b.MinimumSize = Size.Empty; b.Dock = DockStyle.Fill;
            return b;
        }
        private void Log(string s)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), s); return; }
            activity.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + Environment.NewLine);
        }
        private void Error(Exception e) { Log("错误: " + e.Message); MessageBox.Show(this, e.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        private void NeedGame() { if (game == null) throw new InvalidOperationException("请先选择游戏 EXE 并点击检测。"); }
        private bool Confirm(string text) { return MessageBox.Show(this, text, "确认目标", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK; }
        private void Open(string path)
        {
            if (!path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !File.Exists(path) && !Directory.Exists(path))
                throw new FileNotFoundException("尚未生成这个文件或目录：" + path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        private void SelectGame()
        {
            using (var dialog = new OpenFileDialog { Filter = "游戏程序 (*.exe)|*.exe", Title = "选择实际游戏 EXE，不要选择 UnityCrashHandler" })
                if (dialog.ShowDialog(this) == DialogResult.OK) { exeBox.Text = dialog.FileName; DetectGame(); }
        }
        internal void SelectPath(string path, bool preview = false)
        {
            if (preview) { timer.Stop(); busy = true; }
            try { exeBox.Text = path; DetectGame(); } finally { busy = false; }
        }
        private void DetectGame()
        {
            game = null; snapshot = displayedSnapshot = null; pending = null; lastSession = null; lastMessage = null; candidates.Rows.Clear(); allRenderers.Rows.Clear();
            RefreshMaterialWindows();
            cachedSnapshot = null; snapshotRead = null; snapshotStamp = syncedProfileStamp = DateTime.MinValue;
            game = DesktopServices.Detect(exeBox.Text.Trim());
            profile = DesktopServices.LoadProfile(game);
            keywords.Text = String.Join(", ", profile.keywords);
            detection.Text = game.runtime + "   |   " + game.arch + "   |   Unity " + game.unity + "   |   " + game.loader + Environment.NewLine + game.reason;
            detection.ForeColor = game.supported ? ForeColor : Color.FromArgb(153, 84, 22);
            RefreshRules(); Log("检测: " + game.exe + " / " + game.runtime + " / " + game.loader);
            if (Directory.Exists(Path.Combine(game.root, "BepInEx", "plugins", "PainReinMosaicOff")))
                Log("检测到旧 PainReinMosaicOff 插件。若仍启用，它可能使临时恢复无效；请退出游戏后自行移出旧插件再验证。");
            Poll();
        }
        private async Task Work(Func<string> work)
        {
            if (busy) return; busy = true;
            foreach (Control c in actions) c.Enabled = false;
            exeBox.Enabled = false;
            try { await Task.Run(work); }
            catch (Exception e) { Error(e); }
            finally { busy = false; foreach (Control c in actions) c.Enabled = true; exeBox.Enabled = true; }
        }
        private async Task InstallScanner()
        {
            NeedGame(); if (!game.supported) throw new InvalidOperationException(game.reason);
            DesktopServices.CheckClosed(game);
            if (!Confirm("将构建并安装扫描插件到：\r\n" + game.PluginDir + "\r\n\r\n自动规则将关闭；" + (profile.backupBeforeChanges ? "保留旧插件备份。" : "不保留旧插件备份。"))) return;
            var installProfile = DesktopServices.Json.Deserialize<Profile>(DesktopServices.Encode(profile)); installProfile.applyRules = false;
            await Work(delegate { string output = DesktopServices.Build(game, installProfile, Log); DesktopServices.Install(game, output, Log, installProfile.backupBeforeChanges); return output; });
            if (DesktopServices.Installed(game)) { profile = DesktopServices.LoadProfile(game); RefreshRules(); }
        }
        private void Launch()
        {
            NeedGame(); if (DesktopServices.IsRunning(game)) { Log("游戏已运行，请进入目标场景。"); return; }
            Process.Start(new ProcessStartInfo(game.exe) { UseShellExecute = true, WorkingDirectory = game.root });
            Log("已请求启动游戏。Steam 等需要平台启动的游戏，请从原平台启动。");
        }
        private void OpenLog() { NeedGame(); Open(Path.Combine(game.root, "BepInEx", "LogOutput.log")); }
        private void Uninstall()
        {
            NeedGame(); DesktopServices.CheckClosed(game);
            if (!DesktopServices.Installed(game)) throw new InvalidOperationException("未发现本工具安装标记。");
            if (!Confirm((profile.backupBeforeChanges ? "将卸载并备份此插件目录：\r\n" : "将删除此插件目录，不保留备份：\r\n") + game.PluginDir + "\r\n\r\n保留游戏目录下的规则和其他插件。")) return;
            profile = DesktopServices.LoadProfile(game); Protocol.AtomicWrite(DesktopServices.LocalProfile(game), DesktopServices.Encode(profile));
            string backup = DesktopServices.Uninstall(game, profile.backupBeforeChanges);
            Log(backup == null ? "已卸载，未保留插件备份。" : "已卸载，原目录备份到: " + backup); snapshot = displayedSnapshot = null; candidates.Rows.Clear(); allRenderers.Rows.Clear(); ShowDetail(); Poll();
        }
        private void ApplyKeywords()
        {
            NeedGame(); profile.keywords = keywords.Text.Split(new[] { ',', '，', ';', '；', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            DesktopServices.SaveProfile(game, profile); Log("已保存关键词；连接的游戏会自动重新扫描。");
        }
        private DataGridView ActiveGrid { get { return tabs != null && tabs.SelectedTab == allTab ? allRenderers : candidates; } }
        private List<Candidate> Selected()
        { return ActiveGrid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag as Candidate).Where(c => c != null).ToList(); }
        private void SendSelected(string action)
        {
            var selected = Selected(); if (selected.Count == 0) throw new InvalidOperationException("请先选中 Renderer 对象行。");
            if (action == "restore" && snapshot != null && snapshot.applyRules && selected.Any(c => c.ruleMatched))
                throw new InvalidOperationException("选中项仍受自动规则控制。请先停用自动规则，再恢复选中。");
            Send(action, selected.Select(c => c.id).ToArray());
        }
        private void Send(string action, int[] ids)
        {
            NeedGame();
            if (pending != null) throw new InvalidOperationException("上一操作尚未收到游戏确认，请稍候。");
            string request = Guid.NewGuid().ToString("N");
            DesktopServices.Send(game, snapshot, action, ids, request);
            pending = request; pendingSince = DateTime.UtcNow;
            Log("已发送 " + action + "，等待游戏确认。");
        }
        private void OpenMaterialDetails()
        {
            try
            {
                var selected = Selected(); if (selected.Count != 1) throw new InvalidOperationException("请只选中一个 Renderer，再打开材质详情。");
                if (displayedSnapshot == null) throw new InvalidOperationException("尚无对象快照。");
                Candidate target = selected[0].Copy(); string session = displayedSnapshot.session;
                MaterialDetailsForm window = materialWindows.FirstOrDefault(w => w.Matches(session, target));
                if (window != null) { window.Activate(); return; }
                window = new MaterialDetailsForm(session, target, (action, slots) => MaterialAction(session, target, action, slots));
                materialWindows.Add(window);
                window.FormClosed += delegate { materialWindows.Remove(window); };
                window.Show(this); RefreshMaterialWindows();
            }
            catch (Exception e) { Error(e); }
        }
        private string MaterialAction(string session, Candidate identity, string action, SlotRule[] slots)
        {
            NeedGame();
            if (pending != null) throw new InvalidOperationException("请等待上一操作的游戏确认。");
            if (!DesktopServices.Live(game, snapshot) || snapshot.session != session) throw new InvalidOperationException("原游戏连接已失效，请重新打开材质详情。");
            Candidate target = snapshot.renderers.Concat(snapshot.candidates).FirstOrDefault(c => c.id == identity.id && c.path == identity.path && c.name == identity.name && c.rendererType == identity.rendererType);
            if (target == null) throw new InvalidOperationException("对象已失效或不在当前快照中。");
            Protocol.ValidateSlots(target, slots);
            if (snapshot.features < 4) throw new InvalidOperationException("请先更新到 0.1.9 扫描插件。");
            if (action == "save")
            {
                var next = DesktopServices.Json.Deserialize<Profile>(DesktopServices.Encode(profile));
                var rulesToSave = new List<Rule>(next.rules); int added = 0;
                foreach (Rule rule in Protocol.ManualSlotRules(target, slots))
                    if (!rulesToSave.Any(r => r.Key() == rule.Key())) { rulesToSave.Add(rule); added++; }
                next.rules = rulesToSave.ToArray(); next.protocol = Math.Max(2, next.protocol);
                DesktopServices.SaveProfile(game, next); profile = next; RefreshRules();
                string message = "已保存 " + added + " 条限定当前完整路径的材质槽规则，重复项跳过。" + (profile.applyRules ? "自动执行已开启。" : "自动执行未开启，可在主界面启用保存的规则。");
                Log(message); return message;
            }
            string request = Guid.NewGuid().ToString("N");
            DesktopServices.SendSlots(game, snapshot, target, slots, action, request);
            pending = request; pendingSince = DateTime.UtcNow; RefreshMaterialWindows();
            Log("已发送材质槽操作 " + action + "；对象：" + target.path); return request;
        }
        private void RefreshMaterialWindows()
        {
            if (materialWindows.Count == 0) return;
            bool live = game != null && snapshot != null && DesktopServices.Live(game, snapshot);
            foreach (MaterialDetailsForm window in materialWindows.ToArray()) if (!window.IsDisposed) window.RefreshSnapshot(snapshot, live, pending != null || busy);
        }
        private void StopAll()
        {
            NeedGame();
            if (pending != null) throw new InvalidOperationException("请等待上一操作确认后再恢复。");
            profile.applyRules = false; profile.autoDiscover = false; DesktopServices.SaveProfile(game, profile); RefreshRules();
            Send("restoreAll", new int[0]);
        }
        private void AddRules()
        {
            NeedGame(); if (!DesktopServices.Live(game, snapshot)) throw new InvalidOperationException("游戏连接已过期，不能从旧列表创建规则。");
            var selected = Selected(); if (selected.Count == 0) throw new InvalidOperationException("请选中已验证的候选对象。");
            var list = new List<Rule>(profile.rules); int added = 0;
            foreach (Candidate c in selected)
            {
                if (snapshot.features >= 2 && c.slots != null)
                {
                    int skipped; Rule[] defaults = Protocol.DefaultRules(profile, new[] { c }, out skipped);
                    if (defaults.Length > 0)
                    {
                        foreach (Rule r in defaults) { r.generated = false; if (!list.Any(existing => existing.Key() == r.Key())) { list.Add(r); added++; } }
                        profile.protocol = Math.Max(2, profile.protocol); continue;
                    }
                }
                // Prefer the shader that matched a keyword; otherwise keep the first known shader.
                string shader = (c.shaders ?? new string[0]).FirstOrDefault(s => profile.keywords.Any(k => !String.IsNullOrEmpty(k) && s.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)) ?? (c.shaders ?? new string[0]).FirstOrDefault() ?? "";
                var rule = new Rule { name = c.name, rendererType = c.rendererType, shader = shader };
                if (!list.Any(r => r.Key() == rule.Key())) { list.Add(rule); added++; }
            }
            profile.rules = list.ToArray(); DesktopServices.SaveProfile(game, profile); RefreshRules();
            Log("已加入 " + added + " 条规则；自动规则状态=" + profile.applyRules);
        }
        private void GenerateDefaults()
        {
            NeedGame();
            if (!DesktopServices.Live(game, snapshot)) throw new InvalidOperationException("请先连接游戏并进入需要处理的场景。");
            if (snapshot.features < 3) throw new InvalidOperationException("请退出游戏，用 0.1.7 重新安装扫描插件，以启用自动补充规则。");
            if (!snapshot.scanComplete) throw new InvalidOperationException("首次扫描尚未完成，请稍候再生成规则。");
            int skipped; Rule[] defaults = Protocol.DefaultRules(profile, snapshot.renderers, out skipped);
            var next = DesktopServices.Json.Deserialize<Profile>(DesktopServices.Encode(profile));
            var list = new List<Rule>(next.rules); int whole = 0, slots = 0;
            foreach (Rule rule in defaults)
                if (!list.Any(r => r.Key() == rule.Key()) && !(next.blockedRuleKeys ?? new string[0]).Contains(rule.Key())) { list.Add(rule); if (rule.mode == "slots") slots++; else whole++; }
            next.rules = list.ToArray(); next.protocol = 3; next.applyRules = true; next.autoDiscover = true; Protocol.Validate(next);
            DesktopServices.SaveProfile(game, next); profile = next; RefreshRules(); tabs.SelectedIndex = 2;
            Log("默认规则已保存并启用：新增关闭 Renderer " + whole + " 条、隐藏材质槽 " + slots + " 条；已有规则保留，重复项跳过。等待游戏读取。");
            Log("已开启自动补充：新场景和新对象将在扫描完成后自动追加规则，无需再次点击。");
            if (snapshot.allTruncated) Log("桌面列表达到上限；游戏内自动补充会继续处理扫描到的对象。");
        }
        private void SetBackupPreference()
        {
            NeedGame();
            var next = DesktopServices.Json.Deserialize<Profile>(DesktopServices.Encode(profile));
            next.backupBeforeChanges = backupChanges.Checked;
            DesktopServices.SaveProfile(game, next); profile = next; RefreshRules();
            Log(next.backupBeforeChanges ? "已开启操作前备份，保存在：" + Path.Combine(game.WorkDir, "backups") : "已关闭操作前备份。");
        }
        private void SetAutoDiscovery()
        {
            NeedGame();
            bool enable = autoDiscover.Checked;
            if (enable && (!DesktopServices.Live(game, snapshot) || snapshot.features < 3))
                throw new InvalidOperationException("请连接安装了 0.1.7 扫描插件的游戏，再开启自动补充。");
            var next = DesktopServices.Json.Deserialize<Profile>(DesktopServices.Encode(profile));
            next.autoDiscover = enable;
            if (enable) { next.protocol = 3; next.applyRules = true; }
            DesktopServices.SaveProfile(game, next); profile = next; RefreshRules();
            Log(enable ? "已开启自动补充并启用规则。" : "已停止补充新规则；已有规则继续按启停状态执行。");
        }
        private void SyncProfile()
        {
            if (!DesktopServices.Installed(game) || !File.Exists(game.ProfilePath)) return;
            DateTime stamp = File.GetLastWriteTimeUtc(game.ProfilePath);
            if (stamp == syncedProfileStamp) return;
            Profile current = DesktopServices.Read<Profile>(game.ProfilePath); Protocol.Validate(current);
            profile = current; syncedProfileStamp = stamp; RefreshRules();
        }
        private void ToggleRules()
        {
            NeedGame(); var selected = rules.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag as Rule).Where(r => r != null).ToArray();
            bool enable = selected.Any(r => !r.enabled);
            foreach (Rule rule in selected) rule.enabled = enable;
            DesktopServices.SaveProfile(game, profile); RefreshRules(); Log((enable ? "启用" : "停用") + "选中规则 " + selected.Length + " 条。");
        }
        private void RefreshRules()
        {
            var selected = new HashSet<string>(rules.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag as Rule).Where(r => r != null).Select(r => r.Key()));
            int scroll = rules.FirstDisplayedScrollingRowIndex;
            rules.Rows.Clear();
            foreach (Rule r in profile.rules)
            {
                string conditions = r.slots == null || r.slots.Length == 0 ? r.shader : String.Join("; ", r.slots.Select(s => "槽" + s.index + " " + s.material + " [" + s.shader + "]"));
                int i = rules.Rows.Add(r.name, r.rendererType, conditions, r.path, r.mode == "slots" ? "隐藏材质槽" : "关闭 Renderer", r.enabled ? "启用" : "停用"); rules.Rows[i].Tag = r;
            }
            rules.ClearSelection();
            foreach (DataGridViewRow row in rules.Rows) row.Selected = selected.Contains(((Rule)row.Tag).Key());
            if (scroll >= 0 && scroll < rules.Rows.Count) rules.FirstDisplayedScrollingRowIndex = scroll;
            syncingAuto = true; autoDiscover.Checked = profile.autoDiscover; backupChanges.Checked = profile.backupBeforeChanges; syncingAuto = false;
            ruleStatus.Text = "共 " + profile.rules.Length + " 条规则；自动执行：" + (profile.applyRules ? "启用" : "停用") + "。误匹配项可停用或移除。\r\n" + (profile.autoDiscover ? "扫描完成后自动追加；已停用和已删除的规则不会自动恢复。" : "自动补充已关闭；已有规则保持不变。");
        }
        private void RemoveRules()
        {
            NeedGame(); var remove = rules.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag as Rule).ToList();
            profile.blockedRuleKeys = (profile.blockedRuleKeys ?? new string[0]).Concat(remove.Select(r => r.Key())).Distinct().ToArray();
            profile.rules = profile.rules.Where(r => !remove.Contains(r)).ToArray(); DesktopServices.SaveProfile(game, profile); RefreshRules(); Log("已移除 " + remove.Count + " 条规则。");
        }
        private void EnableRules()
        {
            NeedGame(); if (profile.rules.Length == 0) throw new InvalidOperationException("请先临时验证候选，并加入规则。");
            profile.applyRules = true; DesktopServices.SaveProfile(game, profile); RefreshRules(); Log("已启用 " + profile.rules.Length + " 条保存规则；等待游戏读取。");
        }
        private void DisableRules()
        { NeedGame(); profile.applyRules = false; profile.autoDiscover = false; DesktopServices.SaveProfile(game, profile); RefreshRules(); Log("自动规则及自动补充已停用。临时关闭的对象可用“恢复选中”或“停止全部并恢复”。"); }
        private void ImportProfile()
        {
            NeedGame(); using (var d = new OpenFileDialog { Filter = "规则配置 (*.json)|*.json" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var p = DesktopServices.Read<Profile>(d.FileName); Protocol.Validate(p);
                if (!Confirm("将导入 " + p.rules.Length + " 条规则并替换当前规则列表。自动执行保持停用。")) return;
                p.backupBeforeChanges = profile.backupBeforeChanges; p.applyRules = false; p.autoDiscover = false;
                DesktopServices.SaveProfile(game, p, true); profile = p; keywords.Text = String.Join(", ", p.keywords); RefreshRules();
            }
        }
        private void ExportProfile()
        {
            NeedGame(); DesktopServices.NoReparse(game.WorkDir); Directory.CreateDirectory(game.WorkDir);
            using (var d = new SaveFileDialog { Filter = "规则配置 (*.json)|*.json", InitialDirectory = game.WorkDir, FileName = Path.GetFileNameWithoutExtension(game.exe) + "-rules.json" })
                if (d.ShowDialog(this) == DialogResult.OK) Protocol.AtomicWrite(d.FileName, DesktopServices.Encode(profile));
        }
        private async Task ExportPatch()
        {
            NeedGame(); if (profile.rules.Length == 0) throw new InvalidOperationException("请先添加并验证至少一条规则，再导出自动补丁。");
            var export = DesktopServices.Json.Deserialize<Profile>(DesktopServices.Encode(profile)); export.applyRules = true;
            await Work(delegate { return DesktopServices.Build(game, export, Log); });
        }
        private void Poll()
        {
            if (busy || game == null) return;
            // Let an in-progress drag or Shift/Ctrl selection finish before touching rows.
            if (Control.MouseButtons != MouseButtons.None && (candidates.ContainsFocus || allRenderers.ContainsFocus)) return;
            try
            {
                string sessionDirectory = Path.GetDirectoryName(game.SnapshotPath);
                if (Directory.Exists(sessionDirectory)) File.WriteAllText(Path.Combine(sessionDirectory, "viewer.heartbeat"), "0.1.7");
                if (snapshotRead != null && snapshotRead.IsCompleted)
                {
                    var completed = snapshotRead; snapshotRead = null;
                    if (completed.IsFaulted) { snapshotStamp = DateTime.MinValue; throw completed.Exception.GetBaseException(); }
                    cachedSnapshot = completed.Result;
                }
                if (File.Exists(game.SnapshotPath))
                {
                    DateTime stamp = File.GetLastWriteTimeUtc(game.SnapshotPath);
                    if (snapshotRead == null && stamp != snapshotStamp)
                    {
                        string path = game.SnapshotPath; snapshotStamp = stamp;
                        snapshotRead = Task.Run(delegate { return new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Deserialize<Snapshot>(Protocol.ReadText(path)); });
                    }
                }
                else cachedSnapshot = null;
                Snapshot s = cachedSnapshot;
                SyncProfile();
                bool live = DesktopServices.Live(game, s);
                connection.Text = !live ? DesktopServices.DiagnoseConnection(game, s) :
                    "已连接  |  扫描 " + s.rendererCount + " 个 Renderer  |  候选 " + s.candidateCount + "  |  自动规则 " + (s.applyRules ? "开启" : "关闭") + (s.truncated ? "  |  仅显示前 1500 项，请缩小关键词" : "");
                if (!live && connection.Text != lastConnectionReason) { lastConnectionReason = connection.Text; Log(connection.Text); }
                if (live) lastConnectionReason = null;
                if (!live) SuspendSnapshot(connection.Text);
                  else
                  {
                    if (!Object.ReferenceEquals(displayedSnapshot, s) || snapshot == null) AcceptSnapshot(s);
                    if (pending != null && pending == s.ack) { Log("游戏确认: " + s.message); pending = null; }
                    if (s.message != lastMessage) { lastMessage = s.message; Log(s.message); }
                }
                if (pending != null && (DateTime.UtcNow - pendingSince).TotalSeconds > 8)
                { pending = null; Log("操作未在 8 秒内确认。游戏可能加载中；不能视为操作成功。"); }
                RefreshMaterialWindows();
                lastReadError = null;
            }
            catch (Exception e)
            {
                SuspendSnapshot("读取连接失败：" + e.Message);
                if (lastReadError != e.Message) { lastReadError = e.Message; Log(connection.Text); }
            }
        }
        private void SuspendSnapshot(string reason)
        {
            snapshot = null;
            connection.Text = reason + (displayedSnapshot == null ? "" : "  |  保留上次列表，仅供查看；恢复连接后可操作。");
            RefreshMaterialWindows();
        }
        private void AcceptSnapshot(Snapshot s)
        {
            Protocol.ValidateSnapshot(s);
            if (lastSession != s.session)
            {
                candidates.Rows.Clear(); allRenderers.Rows.Clear(); pending = null; lastSession = s.session;
                discoveryOrder.Clear(); nextDiscovery = 0;
                Log("连接到新游戏会话。");
            }
            snapshot = displayedSnapshot = s;
            UpdateCandidates(s);
            RefreshMaterialWindows();
        }
        private void UpdateCandidates(Snapshot s)
        {
            if (tabs.SelectedTab == allTab) UpdateAllTable(s);
            else if (tabs.SelectedIndex == 0) UpdateGrid(candidates, s.candidates);
            ShowDetail();
        }
        private void UpdateAllTable(Snapshot s)
        {
            if (changingFilters) return;
            Candidate[] catalog = s.renderers ?? new Candidate[0];
            var liveIds = new HashSet<int>(catalog.Select(c => c.id));
            foreach (int id in discoveryOrder.Keys.Where(id => !liveIds.Contains(id)).ToArray()) discoveryOrder.Remove(id);
            foreach (Candidate c in catalog) if (!discoveryOrder.ContainsKey(c.id)) discoveryOrder[c.id] = nextDiscovery++;
            string type = allType.SelectedItem as string ?? "全部组件";
            string[] types = new[] { "全部组件" }.Concat(catalog.Select(c => c.rendererType).Where(t => !String.IsNullOrEmpty(t)).Distinct().OrderBy(t => t, StringComparer.OrdinalIgnoreCase)).ToArray();
            // Retain an unavailable selected type until the user clears it, so a scene change cannot broaden an active filter.
            if (!types.Contains(type)) types = types.Concat(new[] { type }).ToArray();
            if (!allType.Items.Cast<string>().SequenceEqual(types))
            {
                changingFilters = true;
                try { allType.Items.Clear(); allType.Items.AddRange(types); allType.SelectedItem = type; }
                finally { changingFilters = false; }
            }
            string filter = allFilter.Text.Trim();
            Candidate[] rows = catalog.Where(c => MatchesState(c) && (type == "全部组件" || c.rendererType == type) &&
                (String.IsNullOrEmpty(filter) || String.Join(" ", new[] { c.name, c.path, c.rendererType, String.Join(" ", c.shaders ?? new string[0]), String.Join(" ", c.materials ?? new string[0]) }).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
            UpdateGrid(allRenderers, rows, CompareAll);
            int[] columns = { -1, 3, 0, 1, 2, 4, 5 };
            int sorted = columns[Math.Max(0, allSort.SelectedIndex)];
            foreach (DataGridViewColumn col in allRenderers.Columns) col.HeaderCell.SortGlyphDirection = col.Index == sorted ? (allDirection.SelectedIndex == 1 ? SortOrder.Descending : SortOrder.Ascending) : SortOrder.None;
            allCount.Text = "显示 " + rows.Length + " / " + s.rendererCount + " 项；状态、组件与文字筛选同时生效。点击列标题也可排序。" + (rows.Length == 0 && catalog.Length > 0 ? " 无匹配项，可点“重置”。" : "") + (s.allTruncated ? " 仅传输前 10000 项。" : "");
        }
        private static int StateRank(Candidate c)
        { return c.hiddenSlots != null && c.hiddenSlots.Length > 0 ? 3 : c.held ? 2 : !c.enabled ? 1 : !c.active ? 4 : 0; }
        private static string StateText(Candidate c)
        { switch (StateRank(c)) { case 1: return "已关闭"; case 2: return "持续关闭"; case 3: return "隐藏槽 " + String.Join(",", c.hiddenSlots); case 4: return "对象未激活"; default: return "已启用"; } }
        private bool MatchesState(Candidate c)
        {
            int state = allState.SelectedIndex;
            if (state <= 0) return true;
            if (state == 5) return !c.active;
            if (state == 6) return c.ruleMatched;
            if (state == 7) return !c.ruleMatched;
            return StateRank(c) == state - 1;
        }
        private int CompareAll(Candidate a, Candidate b)
        {
            int sort = allSort.SelectedIndex, result;
            if (sort <= 0) result = discoveryOrder[a.id].CompareTo(discoveryOrder[b.id]);
            else if (sort == 1) result = StateRank(a).CompareTo(StateRank(b));
            else
            {
                result = StringComparer.OrdinalIgnoreCase.Compare(SortValue(a, sort), SortValue(b, sort));
            }
            if (result != 0) return allDirection.SelectedIndex == 1 ? -Math.Sign(result) : result;
            return discoveryOrder[a.id].CompareTo(discoveryOrder[b.id]);
        }
        private static string SortValue(Candidate c, int sort)
        { return sort == 2 ? c.name : sort == 3 ? c.rendererType : sort == 4 ? String.Join("; ", c.shaders ?? new string[0]) : sort == 5 ? c.evidence : c.path; }
        private void RefreshAllView()
        {
            if (!changingFilters && displayedSnapshot != null)
            {
                UpdateAllTable(displayedSnapshot);
                // A deliberate filter/sort change starts at the top; background snapshots keep the viewport anchor.
                if (allRenderers.Rows.Count > 0) allRenderers.FirstDisplayedScrollingRowIndex = 0;
                ShowDetail();
            }
        }
        private void ResetAllFilters()
        {
            changingFilters = true;
            try { allFilter.Clear(); allState.SelectedIndex = allType.SelectedIndex = allSort.SelectedIndex = allDirection.SelectedIndex = 0; }
            finally { changingFilters = false; }
            RefreshAllView();
        }
        private void SortAllColumn(int column)
        {
            int[] sorts = { 2, 3, 4, 1, 5, 6 }; int selected = sorts[column];
            changingFilters = true;
            try { allDirection.SelectedIndex = allSort.SelectedIndex == selected ? 1 - allDirection.SelectedIndex : 0; allSort.SelectedIndex = selected; }
            finally { changingFilters = false; }
            RefreshAllView();
        }
        private class RendererRowComparer : System.Collections.IComparer
        {
            private readonly Comparison<Candidate> compare;
            public RendererRowComparer(Comparison<Candidate> comparison) { compare = comparison; }
            public int Compare(object x, object y) { return compare((Candidate)((DataGridViewRow)x).Tag, (Candidate)((DataGridViewRow)y).Tag); }
        }
        private void UpdateGrid(DataGridView grid, Candidate[] items, Comparison<Candidate> comparison = null)
        {
            var selected = new HashSet<int>(grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag as Candidate).Where(c => c != null).Select(c => c.id));
            int scroll = grid.FirstDisplayedScrollingRowIndex, horizontal = grid.HorizontalScrollingOffset;
            DataGridViewRow top = scroll >= 0 ? grid.Rows[scroll] : null;
            DataGridViewCell current = grid.CurrentCell;
            var incoming = new HashSet<int>(items.Select(c => c.id));
            updatingGrid = true;
            grid.SuspendLayout();
            try
            {
                // Instance IDs are scoped to a game session. Keep surviving row objects and
                // their order even if the plugin sends the same IDs in a different order.
                for (int i = grid.Rows.Count - 1; i >= 0; i--)
                    if (!incoming.Contains(((Candidate)grid.Rows[i].Tag).id)) grid.Rows.RemoveAt(i);
                var existing = grid.Rows.Cast<DataGridViewRow>().ToDictionary(r => ((Candidate)r.Tag).id);
                foreach (Candidate c in items)
                {
                    DataGridViewRow row;
                    if (!existing.TryGetValue(c.id, out row))
                    {
                        row = new DataGridViewRow(); row.CreateCells(grid); row.Tag = c;
                        int index = grid.Rows.Add(row); row = grid.Rows[index]; existing.Add(c.id, row);
                    }
                    row.Tag = c;
                    object[] cells = { c.name, c.rendererType, String.Join("; ", c.shaders ?? new string[0]),
                        StateText(c), c.evidence, c.path };
                    for (int i = 0; i < cells.Length; i++)
                        if (!Object.Equals(row.Cells[i].Value, cells[i])) row.Cells[i].Value = cells[i];
                }
                if (comparison != null)
                    for (int i = 1; i < grid.Rows.Count; i++)
                        if (comparison((Candidate)grid.Rows[i - 1].Tag, (Candidate)grid.Rows[i].Tag) > 0) { grid.Sort(new RendererRowComparer(comparison)); break; }
                // Deleting the focused row can cause WinForms to auto-select a neighbour.
                // Never let that silently change the targets of a later action.
                DataGridViewCell desired = current != null && current.DataGridView == grid ? current : null;
                if (grid.CurrentCell != desired) grid.CurrentCell = desired;
                foreach (DataGridViewRow row in grid.Rows)
                {
                    bool keep = selected.Contains(((Candidate)row.Tag).id);
                    if (row.Selected != keep) row.Selected = keep;
                }
                int targetScroll = top != null && top.DataGridView == grid ? top.Index : Math.Min(scroll, grid.Rows.Count - 1);
                if (targetScroll >= 0 && grid.FirstDisplayedScrollingRowIndex != targetScroll) grid.FirstDisplayedScrollingRowIndex = targetScroll;
                if (grid.HorizontalScrollingOffset != horizontal) grid.HorizontalScrollingOffset = horizontal;
            }
            finally { grid.ResumeLayout(); updatingGrid = false; }
        }
        private void ShowDetail()
        {
            if (updatingGrid) return;
            TextBox box = ActiveGrid == allRenderers ? allDetail : detail;
            var selected = Selected();
            if (selected.Count == 0) { box.Text = "选中 Renderer 后显示组件、材质、路径和关闭状态。"; return; }
            Candidate c = selected[0];
            string text = "组件：" + c.rendererType + "；Shader：" + String.Join("; ", c.shaders ?? new string[0]) + Environment.NewLine +
                "路径：" + c.path + Environment.NewLine + "材质：" + String.Join("; ", c.materials ?? new string[0]) + Environment.NewLine +
                "Renderer.enabled=" + c.enabled + "；规则命中=" + c.ruleMatched + "；隐藏槽=" + String.Join(",", c.hiddenSlots ?? new int[0]) + "；选中 " + selected.Count + " 项";
            if (box.Text != text) box.Text = text;
        }
        internal void Preview(string path, bool demo, bool all = false)
        {
            ShowInTaskbar = false; Opacity = 0;
            Show(); Application.DoEvents();
            if (demo)
            {
                timer.Stop(); connection.Text = "界面预览（模拟数据，未连接游戏）";
                var c = new Candidate { id = 1, name = "モザイク_1", rendererType = "SkinnedMeshRenderer", shaders = new[] { "Shader Graphs/URPMosaic" },
                    materials = new[] { "MosaicMaterial" }, path = "ExampleScene:/PLAYER/Root/Hip/モザイク_1", enabled = true, originalEnabled = true, active = true, evidence = "对象名: モザ" };
                var body = new Candidate { id = 2, name = "Body", rendererType = "SkinnedMeshRenderer", shaders = new[] { "Universal Render Pipeline/Lit" }, materials = new[] { "Body" }, path = "ExampleScene:/PLAYER/Body", enabled = true, active = true, originalEnabled = true };
                c.slots = new[] { new MaterialSlot { index = 0, present = true, material = "MosaicMaterial", shader = "Shader Graphs/URPMosaic" } };
                body.slots = new[] { new MaterialSlot { index = 0, present = true, material = "Body", shader = "Lit" }, new MaterialSlot { index = 1, present = true, material = "Mask", shader = "Ist/MosaicField" } };
                Candidate[] demoItems = new[] { c, body };
                if (all)
                {
                    c.enabled = false; c.held = c.ruleMatched = true;
                    body.hiddenSlots = new[] { 1 }; body.ruleMatched = true;
                    demoItems = demoItems.Concat(new[] {
                        new Candidate { id = 3, name = "SceneFloor", rendererType = "MeshRenderer", shaders = new[] { "Standard" }, enabled = true, active = true, path = "ExampleScene:/Floor" },
                        new Candidate { id = 4, name = "Effect_01", rendererType = "ParticleSystemRenderer", shaders = new[] { "Particles/Additive" }, enabled = false, active = true, path = "ExampleScene:/Effects/Effect_01" },
                        new Candidate { id = 5, name = "CostumePreview", rendererType = "SkinnedMeshRenderer", shaders = new[] { "Lit" }, enabled = true, active = false, path = "ExampleScene:/Preview" }
                    }).ToArray();
                }
                var demoState = new Snapshot { candidates = new[] { c }, candidateCount = 1, renderers = demoItems, rendererCount = demoItems.Length };
                displayedSnapshot = demoState;
                UpdateCandidates(demoState);
                int skipped; profile = new Profile { protocol = 2, rules = Protocol.DefaultRules(new Profile(), demoState.renderers, out skipped) };
                RefreshRules(); tabs.SelectedIndex = all ? 1 : 2; if (all) allSort.SelectedIndex = 1; Application.DoEvents();
            }
            using (var bmp = new Bitmap(Width, Height)) { DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height)); bmp.Save(path); }
            Close();
        }
    }
    internal static class Program
    {
        [STAThread] private static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--self-test") return SelfTests.Run();
                if (args.Length > 0 && args[0] == "--ui-test") { Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); return SelfTests.RunUi(); }
                if (args.Length > 1 && args[0] == "--build-scanner")
                { var g = DesktopServices.Detect(args[1]); Console.WriteLine(DesktopServices.Build(g, new Profile(), Console.WriteLine, args.Length > 2 ? args[2] : null)); return 0; }
                if (args.Length > 2 && args[0] == "--build-profile")
                { var g = DesktopServices.Detect(args[1]); var p = DesktopServices.Read<Profile>(args[2]); Console.WriteLine(DesktopServices.Build(g, p, Console.WriteLine, args.Length > 3 ? args[3] : null)); return 0; }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (var form = new MainForm())
                {
                    if (args.Length > 2 && (args[0] == "--preview" || args[0] == "--preview-demo" || args[0] == "--preview-all")) { form.SelectPath(args[1], true); form.Preview(args[2], args[0] != "--preview", args[0] == "--preview-all"); return 0; }
                    Application.Run(form);
                }
                return 0;
            }
            catch (Exception e)
            {
                string output = Path.Combine(DesktopServices.Home, "last-error.txt"); File.WriteAllText(output, e.ToString());
                if (args.Length == 0) MessageBox.Show(e.Message + "\r\n诊断: " + output, "Mosaic Toolkit");
                return 1;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MosaicToolkit
{
    public sealed class MaterialDetailsForm : Form
    {
        public readonly string Session;
        public readonly Candidate Identity;
        private readonly Func<string, SlotRule[], string> perform;
        private readonly TextBox heading = new TextBox();
        private readonly Label status = new Label(), result = new Label();
        private readonly DataGridView slots = new DataGridView();
        private readonly Button hide = new Button(), restore = new Button(), save = new Button();
        private string waitingRequest;
        private bool updating, canOperate, rendererEnabled;

        public MaterialDetailsForm(string session, Candidate target, Func<string, SlotRule[], string> action)
        {
            Session = session; Identity = target.Copy(); perform = action;
            Text = "材质详情 · " + target.name; Font = new Font("Microsoft YaHei UI", 9F);
            ClientSize = new Size(900, 530); MinimumSize = new Size(720, 420);
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96F, 96F);
            StartPosition = FormStartPosition.CenterParent; BackColor = Color.FromArgb(244, 247, 250);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 6 };
            foreach (float height in new[] { 72f, 50f }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            foreach (float height in new[] { 44f, 48f, 44f }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            Controls.Add(layout);
            heading.Multiline = heading.ReadOnly = true; heading.Dock = DockStyle.Fill; heading.ScrollBars = ScrollBars.Vertical; heading.TabStop = false;
            heading.Text = target.name + "  ·  " + target.rendererType + Environment.NewLine + target.path;
            layout.Controls.Add(heading, 0, 0);
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; layout.Controls.Add(status, 0, 1);
            slots.Dock = DockStyle.Fill; slots.BackgroundColor = Color.White; slots.RowHeadersVisible = false;
            slots.AllowUserToAddRows = slots.AllowUserToDeleteRows = false; slots.AutoGenerateColumns = false;
            slots.SelectionMode = DataGridViewSelectionMode.FullRowSelect; slots.RowTemplate.Height = 29;
            slots.ColumnHeadersHeight = 32; slots.EnableHeadersVisualStyles = false;
            slots.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(232, 238, 244);
            slots.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 250, 252);
            slots.Columns.Add(new DataGridViewCheckBoxColumn { Name = "choose", HeaderText = "选择", Width = 55 });
            foreach (string[] column in new[] { new[] { "index", "槽编号" }, new[] { "material", "材质名" }, new[] { "shader", "Shader" }, new[] { "state", "状态" } })
                slots.Columns.Add(new DataGridViewTextBoxColumn { Name = column[0], HeaderText = column[1], ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable,
                    Width = column[0] == "index" ? 70 : column[0] == "state" ? 100 : 280 });
            slots.CurrentCellDirtyStateChanged += delegate { if (slots.IsCurrentCellDirty) slots.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            slots.CellValueChanged += delegate { if (!updating) UpdateButtons(); };
            layout.Controls.Add(slots, 0, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            hide.Text = "临时隐藏所选槽"; restore.Text = "恢复所选槽"; save.Text = "保存为规则";
            foreach (Button button in new[] { hide, restore, save }) { button.AutoSize = true; button.Height = 34; button.Padding = new Padding(6, 0, 6, 0); buttons.Controls.Add(button); }
            hide.Click += delegate { Execute("slotsOff"); }; restore.Click += delegate { Execute("slotsRestore"); }; save.Click += delegate { Execute("save"); };
            layout.Controls.Add(buttons, 0, 3);
            result.Dock = DockStyle.Fill; result.Text = "勾选材质槽后操作。相同材质名也可按槽编号分别隐藏。"; layout.Controls.Add(result, 0, 4);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, ForeColor = Color.DimGray,
                Text = "关闭此窗口保留临时效果；主界面“停止全部并恢复”可撤销。\r\n保存规则默认限定此对象路径；主列表换选其他对象不会切换本窗口。" }, 0, 5);
            UpdateTarget(target); slots.CurrentCell = null; slots.ClearSelection(); SetAvailable(false, "等待新鲜游戏连接。");
        }
        public bool Matches(string session, Candidate target)
        { return session == Session && target != null && Identity.id == target.id && Identity.path == target.path && Identity.name == target.name && Identity.rendererType == target.rendererType; }
        private static bool SameSlot(MaterialSlot a, MaterialSlot b)
        { return a != null && b != null && a.index == b.index && a.present == b.present && a.material == b.material && a.shader == b.shader; }
        private void UpdateTarget(Candidate target)
        {
            int scroll = slots.FirstDisplayedScrollingRowIndex, horizontal = slots.HorizontalScrollingOffset;
            updating = true;
            try
            {
                var current = (target.slots ?? new MaterialSlot[0]).Where(s => s != null).ToDictionary(s => s.index);
                for (int i = slots.Rows.Count - 1; i >= 0; i--) if (!current.ContainsKey(((MaterialSlot)slots.Rows[i].Tag).index)) slots.Rows.RemoveAt(i);
                var rows = slots.Rows.Cast<DataGridViewRow>().ToDictionary(r => ((MaterialSlot)r.Tag).index);
                foreach (MaterialSlot slot in current.Values.OrderBy(s => s.index))
                {
                    DataGridViewRow row;
                    if (!rows.TryGetValue(slot.index, out row)) { int index = slots.Rows.Add(false, slot.index, slot.material, slot.shader, ""); row = slots.Rows[index]; }
                    if (!SameSlot(row.Tag as MaterialSlot, slot)) row.Cells[0].Value = false;
                    row.Tag = slot;
                    bool valid = slot.present && !String.IsNullOrEmpty(slot.material) && !String.IsNullOrEmpty(slot.shader);
                    row.Cells[0].ReadOnly = !valid;
                    object[] values = { slot.index, slot.material, slot.shader, !slot.present ? "空材质槽" : (target.hiddenSlots ?? new int[0]).Contains(slot.index) ? "已隐藏" : "未隐藏" };
                    for (int i = 0; i < values.Length; i++) if (!Object.Equals(row.Cells[i + 1].Value, values[i])) row.Cells[i + 1].Value = values[i];
                    row.DefaultCellStyle.ForeColor = valid ? Color.Black : Color.Gray;
                }
                if (scroll >= 0 && slots.Rows.Count > 0) slots.FirstDisplayedScrollingRowIndex = Math.Min(scroll, slots.Rows.Count - 1);
                slots.HorizontalScrollingOffset = horizontal;
            }
            finally { updating = false; }
        }
        public void RefreshSnapshot(Snapshot snapshot, bool connected, bool commandPending)
        {
            if (snapshot == null || !connected) { SetAvailable(false, "连接已过期或游戏已切换，保留详情供查看，操作已禁用。"); return; }
            if (snapshot.session != Session) { SetAvailable(false, "游戏会话已变化，请重新打开对象的材质详情。"); return; }
            Candidate target = (snapshot.renderers ?? new Candidate[0]).Concat(snapshot.candidates ?? new Candidate[0]).FirstOrDefault(c => Matches(Session, c));
            if (target == null) { SetAvailable(false, "对象已失效或不在当前快照中；请在主列表重新定位，操作已禁用。"); return; }
            UpdateTarget(target);
            rendererEnabled = target.enabled;
            if (waitingRequest != null && snapshot.ack == waitingRequest) { result.Text = snapshot.message; waitingRequest = null; }
            else if (waitingRequest != null && !commandPending) { result.Text = "未收到本次操作确认，请检查游戏状态；不能视为操作成功。"; waitingRequest = null; }
            string message = "Renderer.enabled=" + target.enabled + "；对象激活=" + target.active + "；共 " + slots.Rows.Count + " 个材质槽。";
            if (!target.enabled) message += "\r\n整个 Renderer 已关闭；请先从主列表恢复，或停用对应规则，再观察单槽效果。";
            else if (!target.active) message += "\r\n对象尚未激活，材质变化暂时可能看不到。";
            if (snapshot.features < 4) { SetAvailable(false, "扫描插件需更新到 0.1.9：退出游戏后重新安装扫描插件。"); return; }
            SetAvailable(!commandPending, commandPending ? "等待游戏确认上一操作…" : message);
        }
        private void SetAvailable(bool available, string message)
        { canOperate = available; status.Text = message; UpdateButtons(); }
        private SlotRule[] CheckedSlots()
        {
            return slots.Rows.Cast<DataGridViewRow>().Where(r => !r.Cells[0].ReadOnly && Object.Equals(r.Cells[0].Value, true))
                .Select(r => (MaterialSlot)r.Tag).OrderBy(s => s.index)
                .Select(s => new SlotRule { index = s.index, material = s.material, shader = s.shader }).ToArray();
        }
        private void UpdateButtons()
        { restore.Enabled = save.Enabled = canOperate && CheckedSlots().Length > 0; hide.Enabled = save.Enabled && rendererEnabled; }
        private void Execute(string action)
        {
            if (!canOperate) return;
            try
            {
                SlotRule[] selected = CheckedSlots(); if (selected.Length == 0) throw new InvalidOperationException("请先勾选材质槽。");
                string response = perform(action, selected);
                if (action == "save") result.Text = response;
                else { waitingRequest = response; result.Text = "操作已发送，等待游戏确认。"; SetAvailable(false, "等待游戏确认上一操作…"); }
            }
            catch (Exception e) { result.Text = "操作未完成：" + e.Message; }
        }
    }
}

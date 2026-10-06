using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace MosaicToolkit
{
    public static class SelfTests
    {
        static readonly List<string> results = new List<string>();
        static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); results.Add("PASS: " + name); }
        static void Reject(Action action, string name)
        { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, name); }
        static object Field(MainForm form, string name)
        { return typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form); }
        static void InvokeForm(MainForm form, string name, params object[] args)
        { typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, args); }
        static Snapshot UiSnapshot(string session, Candidate[] items)
        { return new Snapshot { session = session, candidates = items, candidateCount = items.Length, renderers = items, rendererCount = items.Length }; }
        static void MaterialUiChecks()
        {
            var target = new Candidate { id = 45, name = "cg_forest", rendererType = "MeshRenderer", path = "Scene_1:/CGSences/cg_forest/cg_forest", active = true, enabled = true,
                slots = Enumerable.Range(0, 5).Select(i => new MaterialSlot { index = i, present = i < 4, material = i < 4 ? "cg_forest_cg_forest" + (i % 2 == 0 ? "" : "2") : "", shader = i < 4 ? "Shader Graphs/SG_hurt" : "" }).ToArray() };
            string sentAction = null; SlotRule[] sentSlots = null;
            using (var window = new MaterialDetailsForm("material-test", target, (action, selected) => { sentAction = action; sentSlots = selected; return action == "save" ? "保存完成" : "request-slots"; }))
            {
                window.ShowInTaskbar = false; window.Opacity = 0; window.Show(); Application.DoEvents();
                Func<string, object> field = name => typeof(MaterialDetailsForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                var table = (DataGridView)field("slots"); var hide = (Button)field("hide"); var restore = (Button)field("restore"); var save = (Button)field("save");
                var state = UiSnapshot("material-test", new[] { target }); state.features = 4;
                window.RefreshSnapshot(state, true, false);
                Check(table.Rows.Count == 5 && table.Rows[4].Cells[0].ReadOnly && !hide.Enabled, "material window lists duplicate names by slot index and disables empty slots");
                table.Rows[2].Cells[0].Value = true; var retained = table.Rows[2];
                hide.PerformClick();
                Check(sentAction == "slotsOff" && sentSlots.Length == 1 && sentSlots[0].index == 2 && !hide.Enabled, "material window sends only checked duplicate-name slot and waits for acknowledgment");
                state.ack = "request-slots"; state.message = "已临时隐藏材质槽 1 项。"; target.hiddenSlots = new[] { 2 };
                window.RefreshSnapshot(state, true, false);
                Check(table.Rows[2] == retained && Object.Equals(retained.Cells[0].Value, true) && (string)retained.Cells[4].Value == "已隐藏" && ((Label)field("result")).Text == state.message, "material refresh retains checked row and displays confirmed hidden state");
                window.Text += "（模拟数据预览）";
                using (var image = new System.Drawing.Bitmap(window.Width, window.Height))
                { window.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, window.Width, window.Height)); image.Save(Path.Combine(DesktopServices.Home, "test-output", "material-details-0.1.9.png")); }
                restore.PerformClick(); Check(sentAction == "slotsRestore" && sentSlots[0].index == 2, "material restore uses the checked slot rather than whole renderer");
                window.RefreshSnapshot(state, true, false); save.PerformClick();
                Check(sentAction == "save" && ((Label)field("result")).Text == "保存完成", "material save action preserves selected slot guards");
                target.slots[2] = new MaterialSlot { index = 2, present = true, material = "Changed", shader = "Lit" }; window.RefreshSnapshot(state, true, false);
                Check(Object.Equals(retained.Cells[0].Value, false) && !hide.Enabled, "changed slot signature clears checkbox instead of targeting replacement material");
                table.Rows[0].Cells[0].Value = true; target.enabled = false; window.RefreshSnapshot(state, true, false); table.Rows[1].Cells[0].Value = true;
                Check(!hide.Enabled && restore.Enabled && save.Enabled && ((Label)field("status")).Text.Contains("整个 Renderer 已关闭"), "whole renderer disabled blocks temporary hide even after checkbox edits");
                window.RefreshSnapshot(state, false, false); Check(!hide.Enabled && !restore.Enabled && !save.Enabled, "disconnected material window is read-only");
                window.RefreshSnapshot(UiSnapshot("other-session", new[] { target }), true, false); Check(!save.Enabled, "pinned material window rejects reused ID in a different game session");
                var other = target.Copy(); other.path = "Scene_2:/different";
                window.RefreshSnapshot(UiSnapshot("material-test", new[] { other }), true, false);
                Check(!save.Enabled && window.Identity.path == "Scene_1:/CGSences/cg_forest/cg_forest", "pinned material window never follows a changed object path");
                state.features = 3; window.RefreshSnapshot(state, true, false); Check(!save.Enabled && ((Label)field("status")).Text.Contains("0.1.9"), "older scanners display actionable upgrade guidance");
                window.Close();
            }
        }
        public static int RunUi()
        {
            try
            {
                using (var form = new MainForm())
                {
                    form.ShowInTaskbar = false; form.Opacity = 0; form.Show();
                    ((Timer)Field(form, "timer")).Stop(); Application.DoEvents();
                    var tabs = (TabControl)Field(form, "tabs");
                    var grid = (DataGridView)Field(form, "allRenderers");
                    var candidateGrid = (DataGridView)Field(form, "candidates");
                    tabs.SelectedTab = (TabPage)Field(form, "allTab"); Application.DoEvents();
                    Candidate[] items = Enumerable.Range(1, 40).Select(i => new Candidate { id = i, name = "Object" + i,
                        path = "Scene:/Object" + i, rendererType = "MeshRenderer", shaders = new[] { "Lit" }, materials = new[] { "Material" }, enabled = true, active = true }).ToArray();
                    InvokeForm(form, "AcceptSnapshot", UiSnapshot("A", items));
                    Check(grid.Rows.Count == 40 && grid.SelectedRows.Count == 0, "initial catalog does not auto-select an actionable target");
                    DataGridViewRow row = grid.Rows[20], top = grid.Rows[15];
                    grid.Columns[5].Width = 700;
                    grid.CurrentCell = row.Cells[0]; grid.ClearSelection(); row.Selected = true; grid.Rows[22].Selected = true;
                    grid.FirstDisplayedScrollingRowIndex = top.Index; grid.HorizontalScrollingOffset = 75;
                    var current = grid.CurrentCell;
                    int selectionEvents = 0; grid.SelectionChanged += delegate { selectionEvents++; };
                    for (int i = 0; i < 20; i++) InvokeForm(form, "AcceptSnapshot", UiSnapshot("A", items.Reverse().ToArray()));
                    Check(Object.ReferenceEquals(grid.Rows[20], row) && grid.CurrentCell == current && grid.SelectedRows.Count == 2 && selectionEvents == 0, "repeated reordered snapshots preserve row identity focus and multi-selection without selection events");
                    Check(grid.Rows[grid.FirstDisplayedScrollingRowIndex] == top && grid.HorizontalScrollingOffset == 75, "refresh preserves vertical anchor and horizontal scroll");
                    items[20].enabled = false;
                    InvokeForm(form, "AcceptSnapshot", UiSnapshot("A", items));
                    Check((string)row.Cells[3].Value == "已关闭" && row.Selected && selectionEvents == 0, "changed state updates in place without resetting selection");
                    var added = new Candidate { id = 100, name = "AA-new", path = "Scene:/AA-new", rendererType = "MeshRenderer" };
                    var extended = new[] { added }.Concat(items).ToArray();
                    InvokeForm(form, "AcceptSnapshot", UiSnapshot("A", extended));
                    Check(((Candidate)grid.Rows[40].Tag).id == 100 && grid.Rows[20] == row && row.Selected, "new object appends without moving existing rows");
                    extended = extended.Where(c => c.id != 1).ToArray();
                    InvokeForm(form, "AcceptSnapshot", UiSnapshot("A", extended));
                    Check(grid.Rows[grid.FirstDisplayedScrollingRowIndex] == top && grid.CurrentCell == current && grid.SelectedRows.Count == 2, "removing an earlier object preserves viewport focus and surviving selection");
                    InvokeForm(form, "SuspendSnapshot", "测试短暂断连");
                    Check(Field(form, "snapshot") == null && grid.Rows.Count == 40 && row.Selected, "temporary disconnect retains rows and selection but removes actionable snapshot");
                    InvokeForm(form, "AcceptSnapshot", UiSnapshot("A", extended));
                    Check(grid.CurrentCell == current && grid.SelectedRows.Count == 2, "same-session reconnect preserves selection and focus");
                    extended = extended.Where(c => c.id != 21 && c.id != 23).ToArray();
                    InvokeForm(form, "AcceptSnapshot", UiSnapshot("A", extended));
                    Check(grid.SelectedRows.Count == 0 && grid.CurrentCell == null, "destroyed selected objects do not transfer selection to neighbours");
                    ((TextBox)Field(form, "allFilter")).Text = "AA-new";
                    Check(grid.Rows.Count == 1 && ((Candidate)grid.Rows[0].Tag).id == 100, "filter continues to work with incremental updates");
                    ((TextBox)Field(form, "allFilter")).Text = "";
                    grid.Rows[0].Selected = true;
                    InvokeForm(form, "AcceptSnapshot", UiSnapshot("B", extended));
                    Check(grid.SelectedRows.Count == 0 && grid.Rows.Count == extended.Length, "new game session clears selection even when instance IDs repeat");
                    tabs.SelectedIndex = 0; Application.DoEvents();
                    var candidateRow = candidateGrid.Rows[2]; candidateGrid.CurrentCell = candidateRow.Cells[0]; candidateGrid.ClearSelection(); candidateRow.Selected = true;
                    InvokeForm(form, "AcceptSnapshot", UiSnapshot("B", extended.Reverse().ToArray()));
                    Check(candidateGrid.Rows[2] == candidateRow && candidateRow.Selected, "candidate tab uses the same stable incremental refresh");
                    tabs.SelectedTab = (TabPage)Field(form, "allTab");
                    var states = Enumerable.Range(0, 6).Select(i => new Candidate { id = 200 + i, name = "Mask" + (5 - i), rendererType = i == 3 ? "SkinnedMeshRenderer" : "MeshRenderer", enabled = i != 1 && i != 2, active = i != 4 && i != 5, held = i == 2, ruleMatched = i == 2 || i == 3, hiddenSlots = i == 3 ? new[] { 1 } : new int[0], path = "Scene:/Mask" + i }).ToArray();
                    states[5].enabled = false;
                    InvokeForm(form, "AcceptSnapshot", UiSnapshot("filters", states));
                    var stateFilter = (ComboBox)Field(form, "allState"); var typeFilter = (ComboBox)Field(form, "allType");
                    var sortFilter = (ComboBox)Field(form, "allSort"); var direction = (ComboBox)Field(form, "allDirection");
                    stateFilter.SelectedIndex = 1; Check(grid.Rows.Count == 1 && ((Candidate)grid.Rows[0].Tag).id == 200, "enabled filter excludes hidden slots held and inactive objects");
                    stateFilter.SelectedIndex = 2; Check(grid.Rows.Count == 2, "closed filter matches plain disabled rows including inactive ones");
                    stateFilter.SelectedIndex = 3; Check(grid.Rows.Count == 1 && ((Candidate)grid.Rows[0].Tag).held, "persistent-close filter is distinct from plain closed");
                    stateFilter.SelectedIndex = 4; Check(grid.Rows.Count == 1 && ((Candidate)grid.Rows[0].Tag).id == 203, "hidden-slot filter includes enabled mixed renderers");
                    stateFilter.SelectedIndex = 5; Check(grid.Rows.Count == 2, "inactive filter includes disabled inactive objects as well");
                    stateFilter.SelectedIndex = 6; Check(grid.Rows.Count == 2, "rule-match filter is independent of renderer enabled state");
                    typeFilter.SelectedItem = "SkinnedMeshRenderer"; ((TextBox)Field(form, "allFilter")).Text = "Mask2";
                    Check(grid.Rows.Count == 1 && ((Candidate)grid.Rows[0].Tag).id == 203, "state component and text filters intersect");
                    stateFilter.SelectedIndex = 7; Check(grid.Rows.Count == 0, "combined nonmatching filters show an empty result instead of broadening selection");
                    InvokeForm(form, "ResetAllFilters");
                    Check(grid.Rows.Count == 6 && stateFilter.SelectedIndex == 0 && typeFilter.SelectedIndex == 0 && sortFilter.SelectedIndex == 0, "reset clears all filters and restores discovery order");
                    var retained = grid.Rows[3]; grid.CurrentCell = retained.Cells[0]; grid.ClearSelection(); retained.Selected = true;
                    sortFilter.SelectedIndex = 2;
                    Check(((Candidate)grid.Rows[0].Tag).name == "Mask0" && retained.Selected && grid.CurrentCell.OwningRow == retained && grid.FirstDisplayedScrollingRowIndex == 0, "deliberate name sorting starts at top while preserving selected row and focus");
                    direction.SelectedIndex = 1;
                    Check(((Candidate)grid.Rows[0].Tag).name == "Mask5", "descending sort reverses primary key");
                    InvokeForm(form, "SortAllColumn", 3);
                    Check(sortFilter.SelectedIndex == 1 && direction.SelectedIndex == 0 && ((Candidate)grid.Rows[0].Tag).id == 200 && grid.Columns[3].HeaderCell.SortGlyphDirection == SortOrder.Ascending, "status header activates semantic state sort and direction indicator");
                    int orderedEvents = 0; grid.SelectionChanged += delegate { orderedEvents++; };
                    InvokeForm(form, "AcceptSnapshot", UiSnapshot("filters", states.Reverse().ToArray()));
                    Check(orderedEvents == 0 && retained.Selected && grid.CurrentCell.OwningRow == retained, "identical sorted refresh avoids resort and preserves selection");
                    states[0].enabled = false; states[4].active = true;
                    InvokeForm(form, "AcceptSnapshot", UiSnapshot("filters", states));
                    Check(((Candidate)grid.Rows[0].Tag).id == 204 && retained.Selected, "changed statuses move to correct sorted positions while retaining selection");
                    stateFilter.SelectedIndex = 1;
                    Check(grid.SelectedRows.Count == 0 && grid.CurrentCell == null, "filtering out selected renderer never transfers selection to a visible neighbour");
                    states[4].enabled = false; InvokeForm(form, "AcceptSnapshot", UiSnapshot("filters", states));
                    Check(grid.Rows.Count == 0 && stateFilter.SelectedIndex == 1 && sortFilter.SelectedIndex == 1, "status changes refresh active filter without resetting chosen sort or filter");
                    InvokeForm(form, "ResetAllFilters");
                    Check(((Candidate)grid.Rows[0].Tag).id == 200, "discovery sort recovers original order after filters and explicit sorting");
                    var fixtureGame = new GameInfo { root = Path.Combine(DesktopServices.Home, "test-output", "ui-rules-" + Guid.NewGuid().ToString("N")), exe = Process.GetCurrentProcess().MainModule.FileName };
                    typeof(MainForm).GetField("game", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, fixtureGame);
                    var pure = new Candidate { id = 1, name = "Mask", rendererType = "MeshRenderer", shaders = new[] { "Mosaic" }, slots = new[] { new MaterialSlot { index = 0, present = true, material = "Mask", shader = "Mosaic" } } };
                    var mixed = new Candidate { id = 2, name = "Body", rendererType = "SkinnedMeshRenderer", shaders = new[] { "Lit", "Mosaic" }, slots = new[] { new MaterialSlot { index = 0, present = true, material = "Body", shader = "Lit" }, new MaterialSlot { index = 1, present = true, material = "Mask", shader = "Mosaic" } } };
                    Snapshot live = UiSnapshot("defaults", new[] { pure, mixed }); live.pid = Process.GetCurrentProcess().Id; live.executable = fixtureGame.exe; live.utc = DateTime.UtcNow.ToString("o"); live.features = 3; live.scanComplete = true;
                    InvokeForm(form, "AcceptSnapshot", live); InvokeForm(form, "GenerateDefaults");
                    var generated = (Profile)Field(form, "profile");
                    Check(generated.protocol == 3 && generated.applyRules && generated.autoDiscover && generated.rules.Length == 2 && generated.rules.Any(r => r.mode == "slots"), "default button enables automatic discovery and displays both rule modes");
                    InvokeForm(form, "GenerateDefaults");
                    Check(((Profile)Field(form, "profile")).rules.Length == 2, "clicking default button again does not duplicate rules");
                    var ruleGrid = (DataGridView)Field(form, "rules"); ruleGrid.Rows[1].Selected = true;
                    InvokeForm(form, "ToggleRules");
                    Check(((Profile)Field(form, "profile")).rules.Count(r => !r.enabled) == 1, "individual generated rule can be disabled for troubleshooting");
                    InvokeForm(form, "RemoveRules");
                    Check(((Profile)Field(form, "profile")).rules.Length == 1 && ((Profile)Field(form, "profile")).blockedRuleKeys.Length == 1, "removed rule is remembered as excluded from automatic generation");
                    InvokeForm(form, "GenerateDefaults");
                    Check(((Profile)Field(form, "profile")).rules.Length == 1, "manual generation also respects removed-rule exclusion");
                    Check(!((CheckBox)Field(form, "backupChanges")).Checked && !Directory.Exists(Path.Combine(fixtureGame.WorkDir, "backups")), "backup checkbox defaults off and generation creates no backups");
                    ((CheckBox)Field(form, "backupChanges")).Checked = true;
                    Check(DesktopServices.LoadProfile(fixtureGame).backupBeforeChanges && Directory.GetFiles(Path.Combine(fixtureGame.WorkDir, "backups"), "*.json").Length == 1, "enabling backups persists game preference and preserves prior rules");
                    ((CheckBox)Field(form, "backupChanges")).Checked = false;
                    Check(!DesktopServices.LoadProfile(fixtureGame).backupBeforeChanges && Directory.GetFiles(Path.Combine(fixtureGame.WorkDir, "backups"), "*.json").Length == 1, "turning backups off creates no additional backup and keeps old backups");
                    ((CheckBox)Field(form, "autoDiscover")).Checked = false;
                    Check(!((Profile)Field(form, "profile")).autoDiscover && ((Profile)Field(form, "profile")).applyRules, "discovery checkbox can stop learning while existing rules remain enabled");
                    InvokeForm(form, "DisableRules");
                    Check(!((Profile)Field(form, "profile")).autoDiscover && !((Profile)Field(form, "profile")).applyRules, "disable action stops automatic discovery as well as rule execution");
                    live.features = 0; Reject(delegate { InvokeForm(form, "GenerateDefaults"); }, "old scanners cannot trigger material slot initialization");
                    live.features = 4; live.utc = DateTime.UtcNow.ToString("o"); pure.path = "Scene:/Mask"; mixed.path = "Scene:/Body";
                    tabs.SelectedIndex = 0; InvokeForm(form, "AcceptSnapshot", live); candidateGrid.ClearSelection(); candidateGrid.Rows[0].Selected = true;
                    InvokeForm(form, "OpenMaterialDetails");
                    var windows = (List<MaterialDetailsForm>)Field(form, "materialWindows");
                    Check(windows.Count == 1 && windows[0].Identity.id == pure.id && !windows[0].Modal, "material detail button opens a modeless window bound to the selected renderer");
                    candidateGrid.ClearSelection(); candidateGrid.Rows[1].Selected = true;
                    Check(windows[0].Identity.id == pure.id, "changing main-list selection does not redirect the material window");
                    var manualSelection = new[] { new SlotRule { index = 0, material = pure.slots[0].material, shader = pure.slots[0].shader } };
                    InvokeForm(form, "MaterialAction", live.session, pure, "save", manualSelection);
                    InvokeForm(form, "MaterialAction", live.session, pure, "save", manualSelection);
                    Profile written = DesktopServices.LoadProfile(fixtureGame);
                    Check(written.rules.Count(r => r.path == pure.path && r.mode == "slots") == 1 && !written.applyRules, "manual slot save persists exact-path rule once without silently enabling all saved rules");
                    windows[0].Close(); Check(windows.Count == 0 && Field(form, "pending") == null, "closing material window neither sends a restore command nor leaves it registered");
                    form.Close();
                }
                MaterialUiChecks();
                results.Add("Passed " + results.Count + " real WinForms refresh checks; no game executed."); return 0;
            }
            catch (Exception e) { results.Add(e.ToString()); return 1; }
            finally
            {
                string output = Path.Combine(DesktopServices.Home, "test-output"); Directory.CreateDirectory(output);
                File.WriteAllLines(Path.Combine(output, "ui-tests.txt"), results);
            }
        }
        public static int Run()
        {
            try
            {
                Profile p = new Profile();
                Check(DesktopServices.ConnectionReason(true, true, false, true, false).Contains("HideManagerGameObject"), "loaded scanner without updates surfaces manager compatibility setting");
                Check(!DesktopServices.ConnectionReason(true, true, false, true, true).Contains("改为 true"), "enabled manager setting is not misdiagnosed");
                Check(DesktopServices.ConnectionReason(false, true, false, false, false).Contains("未安装"), "missing scanner is distinguished from compatibility failure");
                Check(DesktopServices.ConnectionReason(true, false, true, true, false).Contains("尚未运行"), "old startup logs do not imply current running game");
                Check(DesktopServices.ConnectionReason(true, true, true, true, true).Contains("过期"), "stale snapshot diagnosed separately");
                Check(Protocol.KeywordEvidence(p, "モザイク_1", new string[0], new string[0]) != "", "Japanese candidate discovery");
                Check(Protocol.KeywordEvidence(p, "Mesh", new[] { "CENSOR_mat" }, new string[0]) != "", "case-insensitive material discovery");
                Check(Protocol.KeywordEvidence(p, "Body", new string[0], new[] { "Shader Graphs/URPMosaic" }) != "", "shader discovery");
                Check(Protocol.KeywordEvidence(p, "Body", new string[0], new[] { "URP/Lit" }) == "", "unrelated renderer excluded");
                var normalSlot = new MaterialSlot { index = 0, present = true, material = "Body", shader = "Lit" };
                var mosaicSlot = new MaterialSlot { index = 1, present = true, material = "Mask", shader = "Ist/MosaicField" };
                var mixedCandidate = new Candidate { name = "Body", rendererType = "SkinnedMeshRenderer", shaders = new[] { "Lit", "Ist/MosaicField" }, slots = new[] { normalSlot, mosaicSlot } };
                var pureCandidate = new Candidate { name = "Mask", rendererType = "MeshRenderer", shaders = new[] { "Ist/MosaicField" }, slots = new[] { new MaterialSlot { index = 0, present = true, material = "Mask", shader = "Ist/MosaicField" } } };
                int skippedDefaults;
                Rule[] defaults = Protocol.DefaultRules(p, new[] { mixedCandidate, pureCandidate, mixedCandidate }, out skippedDefaults);
                Check(defaults.Length == 2 && defaults[0].mode == "slots" && defaults[0].slots[0].index == 1 && defaults[1].mode == "renderer", "default rules deduplicate and distinguish mixed slots from standalone masks");
                Check(defaults[0].Matches(mixedCandidate) && defaults[1].Matches(pureCandidate), "default rules retain exact material and shader guards");
                pureCandidate.slots = new[] { pureCandidate.slots[0], normalSlot };
                Check(!defaults[1].Matches(pureCandidate), "generated renderer rule refuses changed mixed-material layout");
                Reject(delegate { Protocol.Validate(new Profile { rules = defaults }); }, "new material guards cannot be saved using legacy protocol");
                Protocol.Validate(new Profile { protocol = 2, rules = defaults });
                var defaultsRoundtrip = JsonCodec.Read<Profile>(DesktopServices.Encode(new Profile { protocol = 2, rules = defaults }));
                Check(defaultsRoundtrip.rules[0].mode == "slots" && defaultsRoundtrip.rules[0].slots[0].shader == "Ist/MosaicField", "material slot rules survive desktop-to-runtime serialization");
                var automatic = new Profile { protocol = 3, autoDiscover = true, rules = new[] { defaults[0] }, blockedRuleKeys = new[] { defaults[1].Key() } }; automatic.rules[0].enabled = false;
                Check(Protocol.AppendRules(automatic, defaults) == 0 && !automatic.rules[0].enabled, "automatic merge preserves disabled rules and deleted-rule exclusions");
                Check(JsonCodec.Read<Profile>(DesktopServices.Encode(automatic)).autoDiscover, "automatic-discovery preference persists through runtime codec");
                Reject(delegate { Protocol.Validate(new Profile { autoDiscover = true }); }, "legacy protocol cannot silently enable automatic discovery");
                defaults[0].enabled = true;
                Rule[] nameOnly = Protocol.DefaultRules(p, new[] { new Candidate { name = "mosaic body", slots = new[] { normalSlot, new MaterialSlot {index=1, present=true, material="Cloth", shader="Lit"} } } }, out skippedDefaults);
                Check(nameOnly.Length == 0, "ambiguous name-only mixed renderer is not blindly disabled");
                var rule = new Rule { name = "モザイク_1", shader = "Shader Graphs/URPMosaic", rendererType = "SkinnedMeshRenderer" };
                Check(rule.Matches("モザイク_1", "SkinnedMeshRenderer", "scene:/x", new[] { rule.shader }), "exact composite rule matches");
                Check(!rule.Matches("モザイク_10", "SkinnedMeshRenderer", "scene:/x", new[] { rule.shader }), "similar names do not overmatch");
                Check(!rule.Matches("モザイク_1", "MeshRenderer", "scene:/x", new[] { rule.shader }), "renderer type constrained");
                Check(!rule.Matches("モザイク_1", "SkinnedMeshRenderer", "scene:/x", new[] { "Lit" }), "shader constrained");
                Check(!new Rule().Matches("Body", "Renderer", "x", null), "empty rule cannot disable entire scene");
                Reject(delegate { Protocol.Validate(new Profile { rules = new[] { new Rule() } }); }, "invalid rules rejected");
                p.rules = new[] { rule };
                string json = DesktopServices.Encode(p); var restored = DesktopServices.Json.Deserialize<Profile>(json);
                Check(restored.rules[0].name == rule.name && !restored.applyRules, "Japanese JSON profile round trip and safe default");
                var codecProfile = JsonCodec.Read<Profile>(json);
                Check(codecProfile.rules.Length == 1 && codecProfile.rules[0].name == rule.name && codecProfile.keywords.Length == 3, "actual runtime codec reads desktop rule arrays and keywords");
                var command = new Command { session = "serialization-test", id = "multi", action = "off", ids = new[] { 12, 34 } };
                var codecCommand = JsonCodec.Read<Command>(DesktopServices.Encode(command));
                Check(codecCommand.ids.Length == 2 && codecCommand.ids[1] == 34, "actual runtime codec preserves multiple selected IDs");
                string sharedReadFile = Path.Combine(DesktopServices.Home, "test-output", "shared-read.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(sharedReadFile)); File.WriteAllText(sharedReadFile, "data");
                using (var held = new FileStream(sharedReadFile, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
                    Check(Protocol.ReadText(sharedReadFile) == "data", "IPC reads permit concurrent reader writer and atomic replacement sharing");
                var c = new Candidate { id = 12, name = "モザイク_1", rendererType = "SkinnedMeshRenderer", path = "Scene:/モザイク_1", shaders = new[] { "Shader Graphs/URPMosaic" }, materials = new[] { "mosaic_material" }, enabled = true };
                Candidate workerCopy = c.Copy(); c.enabled = false;
                Check(workerCopy.enabled, "background snapshot data is detached from mutable runtime status");
                var snap = new Snapshot { session = "serialization-test", candidateCount = 2, candidates = new[] { c, c }, rendererCount = 2, renderers = new[] { c, c } };
                var decoded = DesktopServices.Json.Deserialize<Snapshot>(JsonCodec.Write(snap));
                Protocol.ValidateSnapshot(decoded);
                Check(decoded.renderers.Length == 2 && decoded.renderers[0].path == c.path, "actual runtime codec preserves complete renderer catalog");
                Check(decoded.candidates.Length == 2 && decoded.candidates[0].name == c.name && decoded.candidates[0].shaders.Length == 1 && decoded.candidates[0].materials.Length == 1, "actual runtime codec emits candidate objects and nested arrays for desktop");
                var broken = DesktopServices.Json.Deserialize<Snapshot>("{\"protocol\":1,\"candidateCount\":2,\"rendererCount\":164}");
                Reject(delegate { Protocol.ValidateSnapshot(broken); }, "regression: real count-only snapshot is rejected with actionable error");
                string fixture = Path.Combine(DesktopServices.Home, "test-output", "fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
                string f = Path.Combine(fixture, "profile.json"); Protocol.AtomicWrite(f, json); Protocol.AtomicWrite(f, json);
                Check(DesktopServices.Read<Profile>(f).rules.Length == 1, "atomic first write and replacement");
                var g = new GameInfo { root = fixture, exe = Path.Combine(fixture, "Fixture.exe") };
                Directory.CreateDirectory(g.PluginDir); File.WriteAllText(Path.Combine(g.PluginDir, "user-file.txt"), "preserve");
                Reject(delegate { DesktopServices.Uninstall(g); }, "foreign plugin directory cannot be moved");
                Check(File.Exists(Path.Combine(g.PluginDir, "user-file.txt")), "foreign file preserved");
                File.WriteAllText(Path.Combine(g.PluginDir, "toolkit-owner.txt"), DesktopServices.Owner);
                string backup = DesktopServices.Uninstall(g, true);
                Check(File.Exists(Path.Combine(backup, "user-file.txt")) && !Directory.Exists(g.PluginDir), "owned uninstall moves to recoverable backup");
                string package = Path.Combine(fixture, "package");
                string packed = Path.Combine(package, "BepInEx", "plugins", Protocol.Folder);
                Directory.CreateDirectory(packed);
                File.Copy(System.Reflection.Assembly.GetExecutingAssembly().Location, Path.Combine(packed, "MosaicToolkit.Scanner.dll"));
                File.WriteAllText(Path.Combine(packed, "profile.json"), json);
                File.WriteAllText(Path.Combine(packed, "toolkit-owner.txt"), DesktopServices.Owner);
                File.WriteAllText(Path.Combine(fixture, "original-game-data.txt"), "unchanged");
                DesktopServices.Install(g, package, delegate(string x) {});
                Check(DesktopServices.Installed(g) && DesktopServices.Sha(Path.Combine(packed, "MosaicToolkit.Scanner.dll")) == DesktopServices.Sha(Path.Combine(g.PluginDir, "MosaicToolkit.Scanner.dll")), "staged installation verifies copied plugin");
                File.WriteAllText(Path.Combine(g.PluginDir, "session-user-data.txt"), "preserve old session");
                DesktopServices.Install(g, package, delegate(string x) {}, true);
                Check(Directory.GetFiles(Path.Combine(g.WorkDir, "backups"), "session-user-data.txt", SearchOption.AllDirectories).Length == 1, "opt-in reinstall preserves old directory in game-local backup");
                int backupCount = Directory.GetDirectories(Path.Combine(g.WorkDir, "backups")).Length;
                DesktopServices.Install(g, package, delegate(string x) {});
                Check(Directory.GetDirectories(Path.Combine(g.WorkDir, "backups")).Length == backupCount && Directory.GetDirectories(Path.Combine(g.WorkDir, "staging")).Length == 0, "default reinstall retains no old plugin or additional backup after successful swap");
                Check(File.Exists(DesktopServices.LocalProfile(g)) && g.OutputDir == Path.Combine(fixture, "MosaicToolkit", "output"), "rule file and patch output default to selected game folder");
                Check(File.ReadAllText(Path.Combine(fixture, "original-game-data.txt")) == "unchanged", "installation preserves unrelated game data");
                string badPackage = Path.Combine(fixture, "bad-package");
                Reject(delegate { DesktopServices.Install(g, badPackage, delegate(string x) {}); }, "incomplete package rejected before replacing installed plugin");
                Check(DesktopServices.Installed(g), "installed plugin preserved after invalid package");
                Check(DesktopServices.Uninstall(g) == null && !Directory.Exists(g.PluginDir) && File.Exists(DesktopServices.LocalProfile(g)), "default uninstall removes only owned plugin and retains game-local rules");
                using (var process = Process.GetCurrentProcess())
                {
                    var liveGame = new GameInfo { exe = process.MainModule.FileName };
                    var state = new Snapshot { pid = process.Id, executable = liveGame.exe, session = "test", utc = DateTime.UtcNow.ToString("o") };
                    Check(DesktopServices.Live(liveGame, state), "fresh snapshot requires correct live process");
                    Reject(delegate { DesktopServices.CheckClosed(liveGame); }, "running target blocks plugin replacement");
                    state.utc = DateTime.UtcNow.AddMinutes(-1).ToString("o");
                    Check(!DesktopServices.Live(liveGame, state), "stale snapshot rejected");
                    state.utc = DateTime.UtcNow.ToString("o"); state.executable += ".other";
                    Check(!DesktopServices.Live(liveGame, state), "different executable rejected");
                }
                string fakeExe = Path.Combine(fixture, "Fake.exe"); File.Copy(System.Reflection.Assembly.GetExecutingAssembly().Location, fakeExe);
                File.WriteAllText(Path.Combine(fixture, "GameAssembly.dll"), "marker"); Directory.CreateDirectory(Path.Combine(fixture, "Fake_Data", "il2cpp_data"));
                var il = DesktopServices.Detect(fakeExe);
                Check(il.runtime == "IL2CPP" && !il.supported, "IL2CPP detected and blocked from Mono build");
                results.Add("Passed " + results.Count + " desktop/protocol checks; fixtures retained.");
                return 0;
            }
            catch (Exception e) { results.Add(e.ToString()); return 1; }
            finally
            {
                string output = Path.Combine(DesktopServices.Home, "test-output"); Directory.CreateDirectory(output);
                File.WriteAllLines(Path.Combine(output, "desktop-tests.txt"), results);
            }
        }
    }
}

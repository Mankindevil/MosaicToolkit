using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using MosaicToolkit;
using UnityEngine;

class RuntimeTests
{
    static int passed, counter;
    static RuntimePlugin plugin;
    static GameInfo game;
    static Snapshot state;
    static void Check(bool value, string description)
    { if (!value) throw new Exception("FAIL: " + description); passed++; Console.WriteLine("PASS: " + description); }
    static void Call(string method) { typeof(RuntimePlugin).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(plugin, null); }
    static void Set(string field, object value) { typeof(RuntimePlugin).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(plugin, value); }
    static object Get(string field) { return typeof(RuntimePlugin).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(plugin); }
    static void WaitWriter()
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while ((int)Get("publishing") != 0) { if (timeout.ElapsedMilliseconds > 10000) throw new Exception("Snapshot writer timeout"); System.Threading.Thread.Sleep(1); }
    }
    static void DrainScan() { int n = 0; while (Get("scanItems") != null) { if (++n > 2000) throw new Exception("Scan did not finish"); Call("LateUpdate"); } }
    static void Tick(float time)
    {
        WaitWriter(); Time.realtimeSinceStartup = time; Call("LateUpdate"); DrainScan(); WaitWriter(); Call("Publish"); WaitWriter();
        state = DesktopServices.Read<Snapshot>(game.SnapshotPath);
    }
    static Renderer Add(string name, string shader, bool active = true, bool enabled = true)
    {
        var r = new SkinnedMeshRenderer { id = ++counter, enabled = enabled,
            gameObject = new GameObject { name = name, activeInHierarchy = active, transform = new Transform { name = name } },
            sharedMaterials = new[] { new Material { name = "test", shader = new Shader { name = shader } } } };
        Resources.world.Add(r); return r;
    }
    static void Send(string action, Renderer r)
    { DesktopServices.Send(game, state, action, r == null ? new int[0] : new[] { r.id }, Guid.NewGuid().ToString("N")); }
    static void SendMaterial(string action, Renderer renderer, params int[] indices)
    {
        Candidate candidate = Array.Find(state.renderers, c => c.id == renderer.id);
        var selected = new List<SlotRule>();
        foreach (int index in indices) { MaterialSlot slot = candidate.slots[index]; selected.Add(new SlotRule { index = index, material = slot.material, shader = slot.shader }); }
        DesktopServices.SendSlots(game, state, candidate, selected.ToArray(), action, Guid.NewGuid().ToString("N"));
    }
    static void Save(Profile p)
    {
        DesktopServices.SaveProfile(game, p);
        // Force a distinct timestamp even on filesystems with coarse write times.
        File.SetLastWriteTimeUtc(game.ProfilePath, DateTime.UtcNow.AddSeconds(counter++));
    }
    static void Main()
    {
        string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime-fixture-" + Guid.NewGuid().ToString("N"));
        game = new GameInfo { root = root, exe = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName };
        Directory.CreateDirectory(game.PluginDir); File.WriteAllText(Path.Combine(game.PluginDir, "toolkit-owner.txt"), DesktopServices.Owner);
        plugin = new RuntimePlugin(); Call("Awake");
        Set("directory", Path.Combine(game.PluginDir, "session")); Set("profileStamp", DateTime.MinValue);
        Directory.CreateDirectory(Path.Combine(game.PluginDir, "session"));
        string heartbeat = Path.Combine(game.PluginDir, "session", "viewer.heartbeat"); File.WriteAllText(heartbeat, "test");
        Save(new Profile()); Call("OnEnable");
        var target = Add("モザイク_1", "Shader Graphs/URPMosaic");
        var inactive = Add("mosaic_inactive", "URP/Lit", false);
        var body = Add("Body", "URP/Lit");
        var off = Add("censor_off", "Censor", true, false);
        var shaderOnly = Add("Overlay", "CensorShader");
        var asset = Add("mosaic_asset", "Mosaic"); asset.gameObject.scene = new UnityEngine.SceneManagement.Scene();
        Tick(1);
        Check(state.candidateCount == 4 && target.enabled && inactive.enabled && shaderOnly.enabled && !off.enabled, "read-only startup finds names shaders and inactive objects without mutation");
        Check(DesktopServices.Live(game, state), "runtime snapshot passes desktop process/session validation");
        Check(Application.runInBackground && state.backgroundRunActive && state.features >= 5, "viewer enables background running by default and reports active adaptation");
        Protocol.ValidateSnapshot(state);
        Check(state.rendererCount == 5 && state.renderers.Length == 5 && Array.Exists(state.renderers, c => c.id == body.id) && !Array.Exists(state.candidates, c => c.id == body.id), "all-renderer catalog includes ordinary objects independently of keywords");
        Check(Array.Exists(state.renderers, c => c.id == inactive.id && !c.active) && !Array.Exists(state.renderers, c => c.id == asset.id), "catalog includes inactive scene renderers and excludes unloaded assets");
        Send("off", target); Tick(1.3f);
        Check(!target.enabled && body.enabled && asset.enabled && !String.IsNullOrEmpty(state.ack), "file command and acknowledgment close only selected target");
        target.enabled = true; UnityEngine.Rendering.RenderPipelineManager.Render();
        Check(!target.enabled, "URP callback reasserts temporary close");
        int scans = Resources.scans; Tick(1.4f);
        Check(Resources.scans == scans, "cached enforcement does not scan every frame");
        Send("restore", target); Tick(1.6f); Check(target.enabled, "temporary restore returns original enabled state");
        Send("off", off); Tick(1.9f); Send("restore", off); Tick(2.2f); Check(!off.enabled, "restore preserves originally disabled state");
        var rule = new Rule { name = target.gameObject.name, shader = "Shader Graphs/URPMosaic", rendererType = "SkinnedMeshRenderer" };
        Save(new Profile { applyRules = true, rules = new[] { rule } }); Tick(2.5f);
        Check(!target.enabled && body.enabled, "live profile reload applies exact saved rule");
        var spawned = Add("モザイク_1", "Shader Graphs/URPMosaic");
        Send("scan", null); Tick(3.5f); Check(!spawned.enabled, "new dynamic target receives saved rule after requested scan");
        var sceneTarget = Add("モザイク_1", "Shader Graphs/URPMosaic"); UnityEngine.SceneManagement.SceneManager.Load(); Tick(3.6f);
        Check(!sceneTarget.enabled, "scene transition schedules immediate discovery");
        Send("restoreAll", null); Tick(3.9f);
        Check(target.enabled && spawned.enabled && !off.enabled && !DesktopServices.Read<Profile>(game.ProfilePath).applyRules, "stop all persists disabled automation and restores changed targets");
        var stale = new Command { session = "previous-session", id = "wrong", action = "off", ids = new[] { target.id } };
        Protocol.AtomicWrite(Path.Combine(game.PluginDir, "session", "command.json"), DesktopServices.Encode(stale)); Tick(4.2f);
        Check(target.enabled && state.ack != "wrong", "previous session command ignored");
        Send("off", target); Tick(4.5f); target.gameObject.name = "ReusedBody"; Tick(4.6f);
        Check(target.enabled, "renamed pooled target released");
        Send("off", inactive); Tick(4.9f); inactive.gameObject.activeInHierarchy = true; inactive.enabled = true; Tick(5.0f);
        Check(!inactive.enabled, "inactive selected target stays off after activation");
        inactive.destroyed = true; Tick(5.7f);
        Check(state.candidateCount == 5 && Array.Find(state.candidates, c => c.id == inactive.id) == null, "destroyed target removed safely");
        Send("off", shaderOnly); Tick(6.0f); Check(!shaderOnly.enabled, "shader-only candidate can be tested");
        File.WriteAllText(game.ProfilePath, "{ broken"); File.SetLastWriteTimeUtc(game.ProfilePath, DateTime.UtcNow.AddMinutes(1)); Tick(6.3f);
        Check(shaderOnly.enabled && !state.applyRules, "invalid profile fails safe and restores temporary state");
        Save(new Profile()); Tick(6.6f); Send("off", shaderOnly); Tick(6.9f); Call("OnDisable");
        Check(shaderOnly.enabled, "plugin disable restores originals");
        Call("OnEnable"); Tick(7.2f); Check(state.session != null, "component re-enable resumes safely");
        Send("off", body); Tick(7.5f);
        Check(!body.enabled && Array.Exists(state.renderers, c => c.id == body.id && c.held), "ordinary renderer selected from full catalog can be temporarily disabled");
        Send("restore", body); Tick(7.8f);
        Check(body.enabled && !Array.Exists(state.candidates, c => c.id == body.id), "restoring ordinary renderer retains full catalog entry without making it a keyword candidate");
        Save(new Profile { applyRules = true, rules = new[] { rule } });
        for (int i = 0; i < 150; i++) Add("Ordinary" + i, "Lit");
        Time.realtimeSinceStartup = 8.2f; Call("LateUpdate");
        Check(Get("scanItems") != null && (int)Get("scanIndex") <= 32, "large catalog is processed in at most 32 objects per frame");
        DrainScan(); WaitWriter();
        Check(((System.Collections.ICollection)Get("controlled")).Count < 10 && ((System.Collections.IDictionary)Get("targets")).Count > 150, "per-frame enforcement visits controlled subset instead of full catalog");
        scans = Resources.scans; Tick(9.2f);
        Check(Resources.scans == scans, "background discovery interval is five seconds instead of subsecond rescans");
        File.SetLastWriteTimeUtc(heartbeat, DateTime.UtcNow.AddMinutes(-1));
        DateTime snapshotWritten = File.GetLastWriteTimeUtc(game.SnapshotPath);
        Time.realtimeSinceStartup = 10f; Set("nextSnapshot", 0f); Call("LateUpdate"); WaitWriter();
        Check(!(bool)Get("viewer") && File.GetLastWriteTimeUtc(game.SnapshotPath) == snapshotWritten, "expired desktop heartbeat stops full snapshot serialization and writes");
        Check(!Application.runInBackground, "expired viewer restores originally paused background behavior");
        var offlineSpawn = Add("モザイク_1", "Shader Graphs/URPMosaic");
        Time.realtimeSinceStartup = 14f; Call("LateUpdate"); DrainScan();
        Check(!offlineSpawn.enabled, "saved rules keep discovering and controlling new objects without desktop viewer");
        Save(new Profile()); Time.realtimeSinceStartup = 14.5f; Call("LateUpdate"); DrainScan(); scans = Resources.scans;
        Time.realtimeSinceStartup = 21f; Call("LateUpdate");
        Check(Resources.scans == scans, "idle plugin with no viewer rules or temporary targets stops discovery");
        File.WriteAllText(heartbeat, "test"); Time.realtimeSinceStartup = 21.5f; Call("LateUpdate"); DrainScan(); WaitWriter();
        Check((bool)Get("viewer") && Resources.scans > scans, "desktop heartbeat resumes discovery and publishing");
        var mixed = Add("MixedBody", "Lit");
        var regular = mixed.sharedMaterials[0];
        var mosaic = new Material { name = "Mask", shader = new Shader { name = "Ist/MosaicField" } };
        mixed.sharedMaterials = new[] { regular, mosaic, null };
        var other = Add("OtherBody", "Lit"); other.sharedMaterials = new[] { regular, mosaic };
        Send("scan", null); Tick(22f);
        int skipped;
        var defaults = Protocol.DefaultRules(new Profile(), new[] { Array.Find(state.renderers, c => c.id == mixed.id) }, out skipped);
        Save(new Profile { protocol = 2, applyRules = true, rules = defaults }); Tick(22.5f);
        Check(mixed.enabled && mixed.sharedMaterials[0] == regular && mixed.sharedMaterials[1] != mosaic && mixed.sharedMaterials[2] == null, "mixed renderer stays enabled while only matching slot is hidden and null slot preserved");
        Check(other.sharedMaterials[1] == mosaic && mosaic.shader.name == "Ist/MosaicField", "slot replacement does not mutate shared source material or other renderers");
        Send("scan", null); Tick(23f);
        var mixedInfo = Array.Find(state.renderers, c => c.id == mixed.id);
        Check(mixedInfo.slots[1].shader == "Ist/MosaicField" && mixedInfo.hiddenSlots.Length == 1 && mixedInfo.ruleMatched, "rescanning hidden slots reports original metadata and retains rule match");
        var changed = new Material { name = "NewClothes", shader = new Shader { name = "Lit" } };
        mixed.sharedMaterials = new[] { regular, changed, null }; Tick(23.5f);
        Check(mixed.enabled && mixed.sharedMaterials[1] == changed, "game replacing a slot with ordinary material releases it instead of hiding new content");
        mixed.sharedMaterials = new[] { regular, mosaic, null }; Tick(24f);
        Check(mixed.sharedMaterials[1] != mosaic, "matching material returning to its slot is hidden again");
        Send("restoreAll", null); Tick(24.5f);
        Check(mixed.enabled && mixed.sharedMaterials[1] == mosaic, "stop all restores exact original slot reference");
        Save(new Profile { protocol = 2, applyRules = true, rules = defaults }); Tick(25f); Call("OnDisable");
        Check(mixed.sharedMaterials[1] == mosaic, "plugin disable restores material slot replacements");
        Call("OnEnable"); Tick(25.5f); UnityEngine.Object.Destroy((Material)Get("hiddenMaterial")); Shader.unavailable = true;
        mixed.sharedMaterials = new[] { regular, mosaic, null }; Tick(26f);
        Check(mixed.enabled && mixed.sharedMaterials[1] == mosaic, "missing invisible shader never falls back to disabling the whole renderer");
        Shader.unavailable = false;
        File.SetLastWriteTimeUtc(heartbeat, DateTime.UtcNow.AddMinutes(-1));
        Save(new Profile { protocol = 3, autoDiscover = true, applyRules = true, keywords = new[] { "autotest" } });
        var automaticTarget = Add("AutoTestMask", "Lit"); UnityEngine.SceneManagement.SceneManager.Load(); Tick(27f);
        var autoProfile = DesktopServices.Read<Profile>(game.ProfilePath);
        Check(!automaticTarget.enabled && Array.Exists(autoProfile.rules, r => r.name == "AutoTestMask"), "new scene automatically generates persists and applies rules without desktop viewer");
        Check(Array.Exists(DesktopServices.Read<Profile>(DesktopServices.LocalProfile(game)).rules, r => r.name == "AutoTestMask") && !Directory.Exists(Path.Combine(game.WorkDir, "backups")), "runtime mirrors learned rules into game folder without creating default backups");
        DateTime savedStamp = File.GetLastWriteTimeUtc(game.ProfilePath); UnityEngine.SceneManagement.SceneManager.Load(); Tick(27.5f);
        Check(savedStamp == File.GetLastWriteTimeUtc(game.ProfilePath), "unchanged scene does not rewrite profile or duplicate rules");
        var learned = Array.Find(autoProfile.rules, r => r.name == "AutoTestMask"); learned.enabled = false;
        Save(autoProfile); Tick(28f); UnityEngine.SceneManagement.SceneManager.Load(); Tick(28.5f);
        Check(automaticTarget.enabled && !Array.Find(DesktopServices.Read<Profile>(game.ProfilePath).rules, r => r.name == "AutoTestMask").enabled, "automatic rescans do not re-enable manually disabled rules");
        autoProfile.rules = Array.FindAll(autoProfile.rules, r => r.name != "AutoTestMask"); autoProfile.blockedRuleKeys = new[] { learned.Key() };
        Save(autoProfile); Tick(29f); UnityEngine.SceneManagement.SceneManager.Load(); Tick(29.5f);
        Check(automaticTarget.enabled && !Array.Exists(DesktopServices.Read<Profile>(game.ProfilePath).rules, r => r.name == "AutoTestMask"), "automatic rescans do not recreate removed excluded rules");
        var later = Add("AutoTestLater", "Lit"); Tick(35f);
        Check(!later.enabled && Array.Exists(DesktopServices.Read<Profile>(game.ProfilePath).rules, r => r.name == "AutoTestLater"), "dynamic objects receive new rules without a scene transition");
        // Simulate a desktop edit made from the profile that predates AutoTestLater.
        autoProfile.applyRules = false; autoProfile.autoDiscover = false; DesktopServices.SaveProfile(game, autoProfile);
        Check(Array.Exists(DesktopServices.Read<Profile>(game.ProfilePath).rules, r => r.name == "AutoTestLater"), "stale desktop save preserves newly learned runtime rules");
        Tick(35.5f); Send("restoreAll", null); Tick(36f);
        var stopped = DesktopServices.Read<Profile>(game.ProfilePath);
        Check(!stopped.autoDiscover && !stopped.applyRules && later.enabled, "stop all disables discovery and restores learned targets");
        var afterStop = Add("AutoTestAfterStop", "Lit"); UnityEngine.SceneManagement.SceneManager.Load(); Tick(37f);
        Check(afterStop.enabled && !Array.Exists(DesktopServices.Read<Profile>(game.ProfilePath).rules, r => r.name == "AutoTestAfterStop"), "scene changes after stop do not resume automatic learning");
        stopped.autoDiscover = stopped.applyRules = stopped.backupBeforeChanges = true; Save(stopped);
        int oldBackups = Directory.GetFiles(Path.Combine(game.WorkDir, "backups"), "*.json").Length;
        UnityEngine.SceneManagement.SceneManager.Load(); Tick(38f);
        Check(Directory.GetFiles(Path.Combine(game.WorkDir, "backups"), "*.json").Length == oldBackups + 1 && !afterStop.enabled, "opt-in backups also preserve rules before runtime learns a new scene");
        Save(new Profile());
        var forest = Add("cg_forest", "Shader Graphs/SG_hurt");
        Material forestA = forest.sharedMaterials[0], forestB = new Material { name = "cg_forest2", shader = forestA.shader };
        forest.sharedMaterials = new[] { forestA, forestB, forestA, forestB };
        UnityEngine.SceneManagement.SceneManager.Load(); Tick(39f);
        SendMaterial("slotsOff", forest, 2); Tick(39.5f);
        Check(forest.enabled && forest.sharedMaterials[0] == forestA && forest.sharedMaterials[2] != forestA && forest.sharedMaterials[1] == forestB, "manual temporary slot hide distinguishes duplicate material references without disabling renderer");
        Check(forestA.shader.name == "Shader Graphs/SG_hurt" && !DesktopServices.Read<Profile>(game.ProfilePath).applyRules, "manual slot operation neither changes source material nor enables automatic rules");
        SendMaterial("slotsOff", forest, 1); Tick(40f);
        Check(forest.sharedMaterials[1] != forestB && forest.sharedMaterials[2] != forestA && forest.sharedMaterials[3] == forestB, "successive manual hides append selected slots independently");
        SendMaterial("slotsRestore", forest, 2); Tick(40.5f);
        Check(forest.sharedMaterials[2] == forestA && forest.sharedMaterials[1] != forestB, "manual restore restores only chosen slot while another temporary slot stays hidden");
        var replacement = new Material { name = "Changed", shader = new Shader { name = "Lit" } };
        var forestMaterials = forest.sharedMaterials; forestMaterials[1] = replacement; forest.sharedMaterials = forestMaterials; Tick(41f);
        Check(forest.sharedMaterials[1] == replacement && Array.Find(state.renderers, c => c.id == forest.id).hiddenSlots.Length == 0, "game material replacement releases obsolete temporary hide");
        forestMaterials = forest.sharedMaterials; forestMaterials[1] = forestB; forest.sharedMaterials = forestMaterials; Tick(41.5f);
        Check(forest.sharedMaterials[1] == forestB, "released temporary slot does not silently reappear when original material returns");
        SendMaterial("slotsOff", forest, 0); forest.gameObject.transform.name = "moved-forest"; Tick(42f);
        Check(forest.sharedMaterials[0] == forestA && state.message.Contains("身份已变化"), "game rejects slot command when object path changed after desktop snapshot");
        forest.gameObject.transform.name = "cg_forest"; UnityEngine.SceneManagement.SceneManager.Load(); Tick(42.5f);
        SendMaterial("slotsOff", forest, 0); forestMaterials = forest.sharedMaterials; forestMaterials[0] = replacement; forest.sharedMaterials = forestMaterials; Tick(43f);
        Check(forest.sharedMaterials[0] == replacement && state.message.Contains("材质槽已变化"), "game validates current material guard before applying a stale slot command");
        forest.sharedMaterials = new[] { forestA, forestB, forestA, forestB }; UnityEngine.SceneManagement.SceneManager.Load(); Tick(43.5f);
        SendMaterial("slotsOff", forest, 0); forest.enabled = false; Tick(44f);
        Check(!forest.enabled && forest.sharedMaterials[0] == forestA && state.message.Contains("整个 Renderer 已关闭"), "manual slot hide refuses disabled renderer without enabling it");
        forest.enabled = true; Tick(44.5f);
        var forestInfo = Array.Find(state.renderers, c => c.id == forest.id);
        var manual = Protocol.ManualSlotRules(forestInfo, new[] { new SlotRule { index = 1, material = forestB.name, shader = forestB.shader.name } });
        Save(new Profile { protocol = 2, applyRules = true, rules = manual }); Tick(45f);
        SendMaterial("slotsRestore", forest, 1); Tick(45.5f);
        Check(forest.sharedMaterials[1] != forestB && state.message.Contains("自动规则控制"), "restoring a rule-controlled slot reports conflict instead of claiming success");
        Send("restoreAll", null); Tick(46f);
        Check(forest.sharedMaterials[1] == forestB, "stop all restores manual saved slot rules");
        UnityEngine.Object.Destroy((Material)Get("hiddenMaterial")); Shader.unavailable = true;
        SendMaterial("slotsOff", forest, 0); Tick(46.5f);
        Check(forest.sharedMaterials[0] == forestA && state.message.Contains("缺少可用"), "manual slot hide reports unavailable invisible material without mutating renderer");
        Shader.unavailable = false; SendMaterial("slotsOff", forest, 0, 2); Tick(47f);
        Send("restoreAll", null); Tick(47.5f);
        Check(forest.sharedMaterials[0] == forestA && forest.sharedMaterials[2] == forestA, "stop all clears all temporary material slot intents and restores references");
        SendMaterial("slotsOff", forest, 3); Tick(48f); Call("OnDisable");
        Check(forest.sharedMaterials[3] == forestB, "plugin disable restores manually hidden material slots");
        Call("OnDisable");
        Check(!Application.runInBackground, "disable releases background lease");
        Save(new Profile { backgroundWhileConnected = false });
        File.WriteAllText(heartbeat, "test"); Call("OnEnable"); Tick(50f);
        Check(!Application.runInBackground, "explicit opt-out leaves background pause unchanged with a live viewer");
        Save(new Profile()); Tick(51f);
        Check(Application.runInBackground, "live preference reload enables background adaptation");
        Save(new Profile { backgroundWhileConnected = false }); Tick(52f);
        Check(!Application.runInBackground, "turning preference off restores original value");
        Application.runInBackground = true; Save(new Profile()); Tick(53f);
        File.SetLastWriteTimeUtc(heartbeat, DateTime.UtcNow.AddMinutes(-1)); Tick(54f);
        Check(Application.runInBackground, "viewer departure preserves games that already ran in background");
        Application.runInBackground = false; Tick(55f);
        Check(!Application.runInBackground, "standalone plugin without viewer never forces background running");
        File.WriteAllText(heartbeat, "test");
        typeof(RuntimePlugin).GetMethod("OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(plugin, new object[] { false });
        Check(Application.runInBackground, "focus-loss callback acquires lease before Unity can pause frames");
        UnityEngine.SceneManagement.SceneManager.Load(); Tick(56f);
        Check(Application.runInBackground, "scene transition preserves active background lease");
        Call("OnDestroy");
        Check(!Application.runInBackground, "destroy restores background setting even without OnDisable");
        Console.WriteLine("Passed " + passed + " runtime/IPC simulation checks. Unity itself was not executed.");
    }
}

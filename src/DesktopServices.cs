using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace MosaicToolkit
{
    public class GameInfo
    {
        public string exe, root, managed, unity, runtime, arch, loader, reason;
        public bool supported;
        public string WorkDir { get { return Path.Combine(root, "MosaicToolkit"); } }
        public string OutputDir { get { return Path.Combine(WorkDir, "output"); } }
        public string PluginDir { get { return Path.Combine(root, "BepInEx", "plugins", Protocol.Folder); } }
        public string ProfilePath { get { return Path.Combine(PluginDir, "profile.json"); } }
        public string SnapshotPath { get { return Path.Combine(PluginDir, "session", "snapshot.json"); } }
    }
    public static class DesktopServices
    {
        public const string Owner = "MosaicToolkit/0.1 owned plugin directory";
        public static string Home = AppDomain.CurrentDomain.BaseDirectory;
        public static JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        public static string Encode(object value) { return Json.Serialize(value); }
        public static T Read<T>(string path) { return Json.Deserialize<T>(Protocol.ReadText(path)); }
        public static string Sha(string path)
        { using (var s = File.OpenRead(path)) using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)).Replace("-", ""); }
        public static string Key(GameInfo g)
        { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(g.exe.ToUpperInvariant()))).Replace("-", "").Substring(0, 16); }
        public static string LocalProfile(GameInfo g) { return Path.Combine(g.WorkDir, "profile.json"); }
        public static string PeArchitecture(string path)
        {
            using (var s = File.OpenRead(path)) using (var r = new BinaryReader(s))
            {
                if (r.ReadUInt16() != 0x5a4d) return "未知";
                s.Position = 0x3c; int offset = r.ReadInt32();
                if (offset < 0 || offset > s.Length - 6) return "未知";
                s.Position = offset;
                if (r.ReadUInt32() != 0x4550) return "未知";
                ushort machine = r.ReadUInt16();
                return machine == 0x8664 ? "x64" : machine == 0x14c ? "x86" : "未知";
            }
        }
        public static GameInfo Detect(string executable)
        {
            executable = Path.GetFullPath(executable);
            if (!File.Exists(executable)) throw new FileNotFoundException("找不到游戏 EXE。", executable);
            var g = new GameInfo { exe = executable, root = Path.GetDirectoryName(executable), runtime = "未知", loader = "未检测到 BepInEx 5" };
            string data = Path.Combine(g.root, Path.GetFileNameWithoutExtension(executable) + "_Data");
            g.managed = Path.Combine(data, "Managed");
            g.arch = PeArchitecture(executable);
            string player = Path.Combine(g.root, "UnityPlayer.dll");
            g.unity = File.Exists(player) ? FileVersionInfo.GetVersionInfo(player).ProductVersion : "未知";
            bool il2cpp = File.Exists(Path.Combine(g.root, "GameAssembly.dll")) && Directory.Exists(Path.Combine(data, "il2cpp_data"));
            bool mono = File.Exists(Path.Combine(g.managed, "mscorlib.dll")) && Directory.Exists(data);
            g.runtime = il2cpp ? "IL2CPP" : mono ? "Mono" : "未识别";
            string loader = Path.Combine(g.root, "BepInEx", "core", "BepInEx.dll");
            bool bep5 = false;
            if (File.Exists(loader))
            {
                Version v = AssemblyName.GetAssemblyName(loader).Version;
                g.loader = "BepInEx " + v; bep5 = v.Major == 5;
            }
            g.supported = mono && !il2cpp && bep5 && (g.arch == "x64" || g.arch == "x86");
            g.reason = g.supported ? "可构建 Mono 扫描插件；实际兼容性需在游戏内验证。" :
                il2cpp ? "第一版只识别 IL2CPP，不生成或安装 IL2CPP 插件。" :
                !mono ? "未识别为支持的 Unity Mono 游戏，请选择真正的游戏 EXE。" :
                !bep5 ? "请先安装适合这款游戏的 BepInEx 5，再点检测。不会覆盖已有加载器。" : "暂不支持这个程序位数。";
            foreach (string name in new[] { "UnityEngine.CoreModule.dll", "System.Runtime.Serialization.dll", "System.Xml.dll", "mscorlib.dll", "System.dll", "System.Core.dll" })
                if (g.supported && !File.Exists(Path.Combine(g.managed, name))) { g.supported = false; g.reason = "缺少 " + name + "；该 Unity/.NET 布局暂不支持。"; }
            if (g.supported && AssemblyName.GetAssemblyName(Path.Combine(g.managed, "mscorlib.dll")).Version.Major < 4)
            { g.supported = false; g.reason = "第一版需要 .NET 4 Mono，暂不支持旧版 .NET 3.5 游戏。"; }
            return g;
        }
        public static bool Installed(GameInfo g)
        { return File.Exists(Path.Combine(g.PluginDir, "toolkit-owner.txt")) && File.ReadAllText(Path.Combine(g.PluginDir, "toolkit-owner.txt")) == Owner; }
        public static void NoReparse(string path)
        {
            string full = Path.GetFullPath(path);
            for (DirectoryInfo d = new DirectoryInfo(full); d != null; d = d.Parent)
                if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("目录包含链接或联接点，请使用实际目录：" + d.FullName);
        }
        public static bool IsRunning(GameInfo g)
        {
            foreach (Process p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(g.exe)))
            {
                using (p)
                {
                    try { if (String.Equals(p.MainModule.FileName, g.exe, StringComparison.OrdinalIgnoreCase)) return true; }
                    catch { return true; } // Cannot establish identity: don't replace a potentially loaded DLL.
                }
            }
            return false;
        }
        public static void CheckClosed(GameInfo g)
        { if (IsRunning(g)) throw new InvalidOperationException("请先完全退出游戏，再安装或卸载扫描插件。"); }
        public static bool Live(GameInfo g, Snapshot s)
        {
            if (s == null || s.protocol != 1 || String.IsNullOrEmpty(s.session) || !String.Equals(s.executable, g.exe, StringComparison.OrdinalIgnoreCase)) return false;
            DateTime utc;
            if (!DateTime.TryParse(s.utc, null, System.Globalization.DateTimeStyles.RoundtripKind, out utc)) return false;
            double seconds = (DateTime.UtcNow - utc.ToUniversalTime()).TotalSeconds;
            if (seconds < -2 || seconds > 5) return false;
            return SnapshotProcessMatches(g, s);
        }
        public static bool SnapshotProcessMatches(GameInfo g, Snapshot s)
        {
            if (s == null || !String.Equals(s.executable, g.exe, StringComparison.OrdinalIgnoreCase)) return false;
            try { using (var p = Process.GetProcessById(s.pid)) return !p.HasExited && String.Equals(p.MainModule.FileName, g.exe, StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }
        public static string ConnectionReason(bool installed, bool running, bool hasSnapshot, bool scannerReady, bool? hideManager)
        {
            if (!installed) return "未安装扫描插件：退出游戏后，点击“安装扫描插件”。";
            if (!running) return "游戏尚未运行或已退出：启动所选游戏并进入目标场景。";
            if (scannerReady && hideManager == false)
                return "插件已加载但未更新：请退出游戏，将 BepInEx.cfg 的 HideManagerGameObject 改为 true 后重启。";
            if (hasSnapshot) return "游戏仍在运行，扫描数据已过期：可能在后台暂停或正在加载。请切回游戏几秒；可开启上方后台运行适配。";
            if (scannerReady) return "插件已启动但未生成快照：回到游戏窗口；仍无效时查看日志中的插件错误。";
            return "游戏运行中，未发现扫描插件启动记录：请检查游戏日志和安装目录。";
        }
        public static string DiagnoseConnection(GameInfo g, Snapshot snapshot)
        {
            bool running = IsRunning(g), ready = false;
            bool? hide = null;
            string cfg = Path.Combine(g.root, "BepInEx", "config", "BepInEx.cfg");
            if (File.Exists(cfg))
            {
                Match m = Regex.Match(File.ReadAllText(cfg), @"(?mi)^\s*HideManagerGameObject\s*=\s*(true|false)\s*$");
                if (m.Success) hide = Boolean.Parse(m.Groups[1].Value);
            }
            string log = Path.Combine(g.root, "BepInEx", "LogOutput.log");
            if (File.Exists(log))
            {
                using (var s = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (s.Length > 65536) s.Seek(-65536, SeekOrigin.End);
                    using (var r = new StreamReader(s))
                    { string text = r.ReadToEnd(); ready = text.Contains("Mosaic Toolkit Scanner") && text.Contains("ready; session="); }
                }
            }
            if (Installed(g) && running && snapshot != null && !SnapshotProcessMatches(g, snapshot))
                return "游戏仍在运行，但快照属于其他或已结束的进程；等待当前游戏插件生成新数据。";
            if (Installed(g) && running && snapshot != null && (snapshot.protocol != 1 || String.IsNullOrEmpty(snapshot.session)))
                return "快照协议或会话无效，请重新安装当前版本扫描插件。";
            return ConnectionReason(Installed(g), running, snapshot != null, ready, hide);
        }
        public static Profile LoadProfile(GameInfo g)
        {
            string path = Installed(g) && File.Exists(g.ProfilePath) ? g.ProfilePath : LocalProfile(g);
            if (!File.Exists(path)) path = Path.Combine(Home, "profiles", Key(g) + ".json");
            if (!File.Exists(path)) return new Profile();
            var p = Read<Profile>(path); Protocol.Validate(p); return p;
        }
        public static void SaveProfile(GameInfo g, Profile p, bool replace = false)
        {
            Protocol.Validate(p);
            NoReparse(g.WorkDir);
            if (Installed(g))
            {
                NoReparse(g.PluginDir);
                using (var lease = Protocol.LockProfile(g.ProfilePath, 1000))
                {
                    if (lease == null) throw new IOException("规则正在保存，请稍后重试。");
                    BackupProfile(g, g.ProfilePath, p);
                    if (!replace && File.Exists(g.ProfilePath))
                    {
                        Profile latest = null;
                        try { latest = Read<Profile>(g.ProfilePath); Protocol.Validate(latest); }
                        catch (ArgumentException) { latest = null; }
                        catch (InvalidDataException) { latest = null; }
                        var learned = new List<Rule>(); if (latest != null) foreach (Rule rule in latest.rules) if (rule.generated) learned.Add(rule);
                        if (learned.Count > 0) p.protocol = Math.Max(p.protocol, 2);
                        Protocol.AppendRules(p, learned);
                    }
                    Protocol.Validate(p); Protocol.AtomicWrite(g.ProfilePath, Encode(p));
                    Protocol.AtomicWrite(LocalProfile(g), Encode(p));
                }
                return;
            }
            else BackupProfile(g, LocalProfile(g), p);
            Protocol.AtomicWrite(LocalProfile(g), Encode(p));
        }
        private static void BackupProfile(GameInfo g, string source, Profile next)
        {
            if (!next.backupBeforeChanges || !File.Exists(source)) return;
            string old = Protocol.ReadText(source);
            if (old == Encode(next)) return;
            string destination = Path.Combine(g.WorkDir, "backups", "rules-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json");
            NoReparse(Path.GetDirectoryName(destination)); Protocol.AtomicWrite(destination, old);
        }
        public static void Send(GameInfo g, Snapshot state, string action, int[] ids, string request)
        {
            if (!Installed(g) || !Live(g, state)) throw new InvalidOperationException("没有新鲜的游戏连接。请启动装有扫描插件的游戏并等待连接。");
            NoReparse(g.PluginDir);
            var c = new Command { session = state.session, id = request, action = action, ids = ids };
            Protocol.AtomicWrite(Path.Combine(g.PluginDir, "session", "command.json"), Encode(c));
        }
        public static void SendSlots(GameInfo g, Snapshot state, Candidate target, SlotRule[] slots, string action, string request)
        {
            if (!Installed(g) || !Live(g, state)) throw new InvalidOperationException("连接已过期，请等待游戏重新连接。");
            if (state.features < 4) throw new InvalidOperationException("请退出游戏，用 0.1.9 重新安装扫描插件后使用材质槽操作。");
            if (action != "slotsOff" && action != "slotsRestore") throw new ArgumentException("未知材质操作。");
            Protocol.ValidateSlots(target, slots); NoReparse(g.PluginDir);
            var command = new Command { session = state.session, id = request, action = action, ids = new[] { target.id },
                targetName = target.name, targetType = target.rendererType, targetPath = target.path, slots = slots };
            Protocol.AtomicWrite(Path.Combine(g.PluginDir, "session", "command.json"), Encode(command));
        }
        private static string Resource(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            { if (s == null) throw new Exception("程序缺少内嵌源文件: " + name); using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd(); }
        }
        private static string Quote(string s) { return "\"" + s.Replace("\"", "\\\"") + "\""; }
        public static string Build(GameInfo g, Profile p, Action<string> log, string outputRoot = null)
        {
            if (!g.supported) throw new InvalidOperationException(g.reason);
            Protocol.Validate(p);
            string output = Path.Combine(outputRoot ?? g.OutputDir, Path.GetFileNameWithoutExtension(g.exe) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            NoReparse(output);
            string plugin = Path.Combine(output, "BepInEx", "plugins", Protocol.Folder);
            Directory.CreateDirectory(plugin);
            string source = Path.Combine(output, "Source"); Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "Shared.cs"), Resource("Toolkit.Shared.cs"), Encoding.UTF8);
            string processName = Path.GetFileName(g.exe).Replace("\\", "\\\\").Replace("\"", "\\\"");
            string code = Resource("Toolkit.RuntimePlugin.cs").Replace("[DefaultExecutionOrder(32000)]", "[BepInProcess(\"" + processName + "\")]\r\n    [DefaultExecutionOrder(32000)]");
            File.WriteAllText(Path.Combine(source, "RuntimePlugin.cs"), code, Encoding.UTF8);
            string compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
            if (!File.Exists(compiler)) compiler = compiler.Replace("Framework64", "Framework");
            if (!File.Exists(compiler)) throw new FileNotFoundException("需要 Windows .NET Framework 4 编译器。", compiler);
            var args = new List<string> { "/nologo", "/target:library", "/optimize+", "/platform:anycpu", "/nostdlib+", "/warnaserror+", "/out:" + Quote(Path.Combine(plugin, "MosaicToolkit.Scanner.dll")) };
            var refs = new List<string>();
            foreach (string n in new[] { "mscorlib.dll", "System.dll", "System.Core.dll", "System.Runtime.Serialization.dll", "System.Xml.dll", "netstandard.dll", "UnityEngine.dll", "UnityEngine.CoreModule.dll" })
            { string path = Path.Combine(g.managed, n); if (File.Exists(path)) refs.Add(path); }
            refs.Add(Path.Combine(g.root, "BepInEx", "core", "BepInEx.dll"));
            foreach (string path in refs) args.Add("/reference:" + Quote(path));
            args.Add(Quote(Path.Combine(source, "Shared.cs"))); args.Add(Quote(Path.Combine(source, "RuntimePlugin.cs")));
            string response = Path.Combine(source, "compile.rsp"); File.WriteAllLines(response, args, Encoding.UTF8);
            var psi = new ProcessStartInfo(compiler, "/noconfig @" + Quote(response)) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = Process.Start(psi))
            {
                var errors = process.StandardError.ReadToEndAsync();
                string text = process.StandardOutput.ReadToEnd(); process.WaitForExit();
                text += errors.Result;
                File.WriteAllText(Path.Combine(source, "build.log"), text, Encoding.UTF8);
                if (text.Trim().Length > 0) log(text.Trim());
                if (process.ExitCode != 0) throw new Exception("目标游戏插件编译失败。诊断已保存到 " + source);
            }
            File.WriteAllText(Path.Combine(plugin, "toolkit-owner.txt"), Owner);
            Protocol.AtomicWrite(Path.Combine(plugin, "profile.json"), Encode(p));
            Protocol.AtomicWrite(Path.Combine(output, "MosaicToolkit", "profile.json"), Encode(p));
            var manifest = new { toolkit = "0.1.11", scanner = "0.1.11", gameExe = g.exe, unity = g.unity, runtime = g.runtime, architecture = g.arch,
                loader = g.loader, pluginSha256 = Sha(Path.Combine(plugin, "MosaicToolkit.Scanner.dll")), ruleCount = p.rules.Length,
                applyRules = p.applyRules, gameExeSha256 = Sha(g.exe), inGameValidated = false };
            File.WriteAllText(Path.Combine(output, "manifest.json"), Encode(manifest), Encoding.UTF8);
            File.WriteAllText(Path.Combine(output, "安装说明.txt"),
                "Mosaic Toolkit 0.1.11 / Scanner 0.1.11 / BepInEx 5 Mono\r\n目标游戏: " + g.exe +
                "\r\n退出游戏，将 BepInEx 文件夹合并到游戏根目录。需要已有 BepInEx 5。\r\n" +
                "自动规则: " + p.applyRules + "；规则数: " + p.rules.Length +
                "\r\n按规则关闭独立 Renderer 或隐藏命中的材质槽。动态对象约每 5 秒发现一次，可能短暂显示。\r\n" +
                "自动补充新场景规则: " + p.autoDiscover + "；已停用或移除的规则不会自动恢复。\r\n" +
                "本包包含扫描插件和配置，桌面程序无需一直运行。扫描插件仍需保留。\r\n" +
                "卸载：退出游戏，将 BepInEx\\plugins\\MosaicToolkit 整个文件夹移出 plugins。\r\n" +
                "验证：BepInEx\\LogOutput.log 应出现 Mosaic Toolkit Scanner；画面结果需在游戏内确认。\r\n" +
                "Source 包含源码及本次编译参数，不包含游戏或 BepInEx 程序集。\r\n", Encoding.UTF8);
            ZipFile.CreateFromDirectory(output, output + ".zip");
            log("已构建并导出: " + output + ".zip"); return output;
        }
        private static void CheckOwnedDirectory(GameInfo g, string directory)
        {
            string full = Path.GetFullPath(directory), basePath = Path.GetFullPath(g.root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) throw new IOException("操作路径超出所选游戏目录。");
            NoReparse(full);
            string owner = Path.Combine(full, "toolkit-owner.txt");
            if (!File.Exists(owner) || File.ReadAllText(owner) != Owner) throw new IOException("目标目录没有本工具的所有权标记，停止操作。");
            CheckTree(full);
        }
        private static void CheckTree(string directory)
        {
            foreach (string path in Directory.GetFileSystemEntries(directory))
            {
                var attr = File.GetAttributes(path);
                if ((attr & FileAttributes.ReparsePoint) != 0) throw new IOException("插件目录包含链接，停止操作。");
                if ((attr & FileAttributes.Directory) != 0) CheckTree(path);
            }
        }
        private static string MoveOldPlugin(GameInfo g, bool keepBackup)
        {
            CheckOwnedDirectory(g, g.PluginDir);
            string backup = Path.Combine(g.WorkDir, keepBackup ? "backups" : "staging", "plugin-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            NoReparse(Path.GetDirectoryName(backup));
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if (!Path.GetFullPath(backup).StartsWith(Path.GetFullPath(g.WorkDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("暂存路径超出工具目录。");
            Directory.Move(g.PluginDir, backup); return backup;
        }
        public static void Install(GameInfo g, string package, Action<string> log, bool keepBackup = false)
        {
            CheckClosed(g); NoReparse(g.PluginDir);
            string source = Path.Combine(package, "BepInEx", "plugins", Protocol.Folder);
            string[] files = { "MosaicToolkit.Scanner.dll", "profile.json", "toolkit-owner.txt" };
            foreach (string f in files) if (!File.Exists(Path.Combine(source, f))) throw new IOException("安装包缺少 " + f);
            if (File.ReadAllText(Path.Combine(source, files[2])) != Owner) throw new IOException("安装包标记无效。");
            if (Directory.Exists(g.PluginDir) && !Installed(g)) throw new IOException("已有同名目录不属于本工具，停止覆盖。");
            string stage = Path.Combine(g.root, "BepInEx", "MosaicToolkitStaging", Guid.NewGuid().ToString("N"));
            NoReparse(stage); Directory.CreateDirectory(stage);
            foreach (string file in files) File.Copy(Path.Combine(source, file), Path.Combine(stage, file), false);
            foreach (string file in files)
                if (Sha(Path.Combine(source, file)) != Sha(Path.Combine(stage, file))) throw new IOException("暂存文件校验失败，尚未替换插件。");
            string backup = null;
            if (Directory.Exists(g.PluginDir)) { backup = MoveOldPlugin(g, keepBackup); if (keepBackup) log("旧插件备份到: " + backup); }
            try { Directory.CreateDirectory(Path.GetDirectoryName(g.PluginDir)); Directory.Move(stage, g.PluginDir); }
            catch
            {
                if (backup != null && !Directory.Exists(g.PluginDir)) { CheckOwnedDirectory(g, backup); NoReparse(g.PluginDir); Directory.Move(backup, g.PluginDir); }
                throw;
            }
            if (backup != null && !keepBackup) { CheckOwnedDirectory(g, backup); Directory.Delete(backup, true); }
            NoReparse(g.WorkDir); Protocol.AtomicWrite(LocalProfile(g), Protocol.ReadText(Path.Combine(g.PluginDir, "profile.json")));
            log("安装并校验成功: " + g.PluginDir);
        }
        public static string Uninstall(GameInfo g, bool keepBackup = false)
        {
            CheckClosed(g); CheckOwnedDirectory(g, g.PluginDir);
            if (keepBackup) return MoveOldPlugin(g, true);
            Directory.Delete(g.PluginDir, true); return null;
        }
    }
}

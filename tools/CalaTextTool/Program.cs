using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.Unversioned;
using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.Kismet.Bytecode.Expressions;
using System.Reflection;

// CalaTextTool: dump / apply FText translations in cooked-then-legacy UE5.7 widget assets.
//   dump  <inputDir> <outJson>
//   apply <inputDir> <mapJson> <outputDir>
// mapJson is { "English source": "中文", ... }. Matching is on the FText SourceString
// (CultureInvariantString), which is what UE displays when no .locres is present.

internal static class Program
{
    static readonly EngineVersion EV = EngineVersion.VER_UE5_7;
    static Usmap Maps = null;

    static UAsset Load(string path) => new UAsset(path, EV, Maps);

    static Usmap TryLoadUsmap(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (!File.Exists(path)) { Console.Error.WriteLine($"usmap not found: {path}"); return null; }
        var m = new Usmap(path);
        Console.Error.WriteLine($"loaded usmap: {path} (schemas={m.Schemas?.Count})");
        return m;
    }

    sealed class Entry
    {
        public string File;
        public string Export;
        public string Prop;
        public string History;
        public string Namespace;
        public string Key;      // base FString Value (Key for Base history)
        public string Source;   // CultureInvariantString (the SourceString shown by UE)
    }

    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            if (args.Length >= 3 && args[0] == "dump") { Maps = TryLoadUsmap(args.Length >= 4 ? args[3] : null); return Dump(args[1], args[2]); }
            if (args.Length >= 3 && args[0] == "dumpbc") { Maps = TryLoadUsmap(args.Length >= 4 ? args[3] : null); return DumpBC(args[1], args[2]); }
            if (args.Length >= 3 && args[0] == "dumpstr") { Maps = TryLoadUsmap(args.Length >= 4 ? args[3] : null); return DumpStr(args[1], args[2]); }
            if (args.Length >= 2 && args[0] == "bccheck") { Maps = TryLoadUsmap(args.Length >= 3 ? args[2] : null); return BcCheck(args[1]); }
            if (args.Length >= 3 && args[0] == "bcswitch") { Maps = TryLoadUsmap(args.Length >= 4 ? args[3] : null); return BcSwitch(args[1], args[2]); }
            if (args.Length >= 3 && args[0] == "bcfuncs") { Maps = TryLoadUsmap(args.Length >= 4 ? args[3] : null); return BcFuncs(args[1], args[2]); }
            if (args.Length >= 3 && args[0] == "bcentry") { Maps = TryLoadUsmap(args.Length >= 4 ? args[3] : null); return BcUberEntry(args[1], args[2]); }
            if (args.Length >= 2 && args[0] == "stats") { Maps = TryLoadUsmap(args.Length >= 3 ? args[2] : null); return Stats(args[1]); }
            if (args.Length >= 4 && args[0] == "apply") { Maps = TryLoadUsmap(args.Length >= 5 ? args[4] : null); return Apply(args[1], args[2], args[3]); }
            Console.Error.WriteLine("usage:\n  dump  <inputDir> <outJson> [usmap]\n  stats <inputDir> [usmap]\n  apply <inputDir> <mapJson> <outputDir> [usmap]");
            return 2;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("FATAL: " + e);
            return 1;
        }
    }

    static IEnumerable<string> UAssets(string dir) =>
        Directory.EnumerateFiles(dir, "*.uasset", SearchOption.AllDirectories);

    // ---- traversal ------------------------------------------------------
    static void Walk(PropertyData p, Action<TextPropertyData> onText)
    {
        switch (p)
        {
            case null: return;
            case TextPropertyData t: onText(t); return;
            case StructPropertyData s:
                if (s.Value != null) foreach (var c in s.Value) Walk(c, onText);
                return;
            case ArrayPropertyData a: // SetPropertyData derives from this
                if (a.Value != null) foreach (var c in a.Value) Walk(c, onText);
                return;
            case MapPropertyData m:
                if (m.Value != null)
                    foreach (var kv in m.Value) { Walk(kv.Key, onText); Walk(kv.Value, onText); }
                return;
        }
    }

    static void ForEachText(UAsset asset, Action<Export, TextPropertyData> cb)
    {
        foreach (var exp in asset.Exports)
            if (exp is NormalExport ne && ne.Data != null)
                foreach (var p in ne.Data)
                    Walk(p, t => cb(exp, t));
    }

    static string Nm(FName n) => n?.Value?.Value;
    static string Fs(FString s) => s?.Value;

    // ---- bytecode FText (EX_TextConst) ---------------------------------
    static string ExprStr(KismetExpression e) => e switch
    {
        EX_StringConst s => s.Value,
        EX_UnicodeStringConst u => u.Value,
        _ => null
    };

    sealed class BcEntry
    {
        public string File, Export, Type, Source, Namespace, Key;
    }

    static void CollectTextConsts(UAsset asset, Action<EX_TextConst> cb)
    {
        foreach (var exp in asset.Exports)
        {
            if (exp is StructExport se && se.ScriptBytecode != null)
            {
                foreach (var top in se.ScriptBytecode)
                {
                    uint off = 0;
                    top.Visit(asset, ref off, (e, o) => { if (e is EX_TextConst tc) cb(tc); });
                }
            }
        }
    }

    static BcEntry Describe(EX_TextConst tc)
    {
        var t = tc.Value;
        return t.TextLiteralType switch
        {
            EBlueprintTextLiteralType.LocalizedText => new BcEntry { Type = "Localized", Source = ExprStr(t.LocalizedSource), Namespace = ExprStr(t.LocalizedNamespace), Key = ExprStr(t.LocalizedKey) },
            EBlueprintTextLiteralType.InvariantText => new BcEntry { Type = "Invariant", Source = ExprStr(t.InvariantLiteralString) },
            EBlueprintTextLiteralType.LiteralString => new BcEntry { Type = "Literal", Source = ExprStr(t.LiteralString) },
            _ => new BcEntry { Type = t.TextLiteralType.ToString() }
        };
    }

    // Visit all expressions in a function's bytecode; returns (expr,startOffset) list and total size.
    static (List<(KismetExpression e, uint o)> list, uint total) VisitAll(UAsset asset, KismetExpression[] code)
    {
        var list = new List<(KismetExpression, uint)>();
        uint off = 0;
        foreach (var top in code) top.Visit(asset, ref off, (e, o) => list.Add((e, o)));
        return (list, off);
    }

    // Verify offset-semantics model on (unedited) bytecode.
    static int BcCheck(string inputDir)
    {
        int funcs = 0, ctxOk = 0, ctxBad = 0, skipOk = 0, skipBad = 0, jmpOk = 0, jmpBad = 0;
        int sizeOk = 0, sizeBad = 0; var sizeBadSamples = new List<string>();
        var badSamples = new List<string>();
        foreach (var path in UAssets(inputDir).OrderBy(x => x))
        {
            string rel = Path.GetRelativePath(inputDir, path).Replace('\\', '/');
            UAsset asset;
            try { asset = Load(path); } catch { continue; }
            foreach (var exp in asset.Exports)
            {
                if (exp is not StructExport se || se.ScriptBytecode == null) continue;
                funcs++;
                var (all, total) = VisitAll(asset, se.ScriptBytecode);
                if ((int)total == se.ScriptBytecodeSize) sizeOk++;
                else { sizeBad++; if (sizeBadSamples.Count < 12) sizeBadSamples.Add($"{rel}:{Nm(exp.ObjectName)} visit={total} stored={se.ScriptBytecodeSize}"); }
                var byStart = new HashSet<uint>();
                foreach (var (e, o) in all) byStart.Add(o);
                bool Aligns(uint t) => t == total || byStart.Contains(t);
                foreach (var (e, o) in all)
                {
                    switch (e)
                    {
                        case EX_Context c:
                            if (c.Offset == c.ContextExpression.GetSize(asset)) ctxOk++;
                            else { ctxBad++; if (badSamples.Count < 8) badSamples.Add($"CTX {rel}:{Nm(exp.ObjectName)} off={c.Offset} size={c.ContextExpression.GetSize(asset)}"); }
                            break;
                        case EX_Skip s:
                            if (s.CodeOffset == s.SkipExpression.GetSize(asset)) skipOk++; else skipBad++;
                            break;
                        case EX_Jump j: if (Aligns(j.CodeOffset)) jmpOk++; else { jmpBad++; if (badSamples.Count < 20) badSamples.Add($"JMP {rel}:{Nm(exp.ObjectName)} tgt={j.CodeOffset} total={total}"); } break;
                        case EX_JumpIfNot j: if (Aligns(j.CodeOffset)) jmpOk++; else { jmpBad++; if (badSamples.Count < 20) badSamples.Add($"JMPIFNOT {rel}:{Nm(exp.ObjectName)} tgt={j.CodeOffset} total={total}"); } break;
                        case EX_PushExecutionFlow p: if (Aligns(p.PushingAddress)) jmpOk++; else { jmpBad++; if (badSamples.Count < 20) badSamples.Add($"PUSH {rel}:{Nm(exp.ObjectName)} tgt={p.PushingAddress} total={total}"); } break;
                        case EX_SwitchValue sw:
                            if (Aligns(sw.EndGotoOffset)) jmpOk++; else { jmpBad++; if (badSamples.Count < 20) badSamples.Add($"SWEND {rel}:{Nm(exp.ObjectName)} tgt={sw.EndGotoOffset} total={total}"); }
                            foreach (var cs in sw.Cases) { if (Aligns(cs.NextOffset)) jmpOk++; else { jmpBad++; if (badSamples.Count < 20) badSamples.Add($"SWCASE {rel}:{Nm(exp.ObjectName)} tgt={cs.NextOffset} total={total}"); } }
                            break;
                    }
                }
            }
        }
        Console.WriteLine($"functions      : {funcs}");
        Console.WriteLine($"bytecode size  : ok={sizeOk} bad={sizeBad}");
        if (sizeBadSamples.Count > 0) Console.WriteLine("  size-bad:\n    " + string.Join("\n    ", sizeBadSamples));
        Console.WriteLine($"EX_Context     : ok={ctxOk} bad={ctxBad}");
        Console.WriteLine($"EX_Skip        : ok={skipOk} bad={skipBad}");
        Console.WriteLine($"abs jumps      : ok={jmpOk} bad={jmpBad}");
        if (badSamples.Count > 0) Console.WriteLine("samples:\n  " + string.Join("\n  ", badSamples));
        return (ctxBad == 0 && skipBad == 0 && jmpBad == 0) ? 0 : 3;
    }

    // Diagnostic: for one function name substring, print switch offsets vs expression starts.
    static int BcSwitch(string inputDir, string fnFilter)
    {
        foreach (var path in UAssets(inputDir).OrderBy(x => x))
        {
            UAsset asset;
            try { asset = Load(path); } catch { continue; }
            foreach (var exp in asset.Exports)
            {
                if (exp is not StructExport se || se.ScriptBytecode == null) continue;
                string fn = Nm(exp.ObjectName) ?? "";
                if (!fn.Contains(fnFilter)) continue;
                var (all, total) = VisitAll(asset, se.ScriptBytecode);
                var starts = all.Select(x => x.o).OrderBy(x => x).ToList();
                var startsSet = new HashSet<uint>(starts);
                Console.WriteLine($"== {Path.GetFileName(path)} :: {fn}  total={total} ==");
                foreach (var (e, o) in all)
                {
                    if (e is EX_SwitchValue sw)
                    {
                        Console.WriteLine($"  SWITCH @{o} EndGoto={sw.EndGotoOffset} (aligned={startsSet.Contains(sw.EndGotoOffset) || sw.EndGotoOffset == total})");
                        // nearest starts around EndGoto
                        var belowList = starts.Where(s => s <= sw.EndGotoOffset).ToList();
                        var aboveList = starts.Where(s => s >= sw.EndGotoOffset).ToList();
                        uint below = belowList.Count > 0 ? belowList.Max() : 0u;
                        uint above = aboveList.Count > 0 ? aboveList.Min() : total;
                        Console.WriteLine($"     nearest starts: below={below} above={above}");
                        for (int i = 0; i < sw.Cases.Length; i++)
                            Console.WriteLine($"     case[{i}] NextOffset={sw.Cases[i].NextOffset} (aligned={startsSet.Contains(sw.Cases[i].NextOffset) || sw.Cases[i].NextOffset == total})");
                    }
                }
            }
        }
        return 0;
    }

    static string ResolveIdx(UAsset asset, FPackageIndex fp)
    {
        try
        {
            if (fp == null || fp.IsNull()) return "<null>";
            if (fp.IsImport()) return Nm(fp.ToImport(asset).ObjectName) ?? "<imp?>";
            if (fp.IsExport()) { var ex = asset.Exports[fp.Index - 1]; return Nm(ex.ObjectName) ?? "<exp?>"; }
        }
        catch { }
        return "<err>";
    }

    static int BcUberEntry(string inputDir, string fnFilter)
    {
        foreach (var path in UAssets(inputDir).OrderBy(x => x))
        {
            string rel = Path.GetFileName(path);
            UAsset asset;
            try { asset = Load(path); } catch { continue; }
            foreach (var exp in asset.Exports)
            {
                if (exp is not StructExport se || se.ScriptBytecode == null) continue;
                string fn = Nm(exp.ObjectName) ?? "";
                if (fnFilter.Length > 0 && !fn.Contains(fnFilter)) continue;
                var (all, _) = VisitAll(asset, se.ScriptBytecode);
                foreach (var (e, o) in all)
                {
                    if (e is EX_FinalFunction ff)
                    {
                        string tgt = ResolveIdx(asset, ff.StackNode);
                        if (tgt != null && tgt.Contains("ExecuteUbergraph"))
                        {
                            var ip = ff.Parameters != null && ff.Parameters.Length > 0 && ff.Parameters[0] is EX_IntConst ic ? ic.Value.ToString() : "?";
                            Console.WriteLine($"{rel}:{fn} @{o} -> {tgt}(EntryPoint={ip})");
                        }
                    }
                }
            }
        }
        return 0;
    }

    static int BcFuncs(string inputDir, string fnFilter)
    {
        foreach (var path in UAssets(inputDir).OrderBy(x => x))
        {
            UAsset asset;
            try { asset = Load(path); } catch { continue; }
            foreach (var exp in asset.Exports)
            {
                if (exp is not StructExport se || se.ScriptBytecode == null) continue;
                string fn = Nm(exp.ObjectName) ?? "";
                if (!fn.Contains(fnFilter)) continue;
                var (all, _) = VisitAll(asset, se.ScriptBytecode);
                foreach (var (e, o) in all)
                {
                    if (e is EX_FinalFunction ff) Console.WriteLine($"@{o} FINAL {ResolveIdx(asset, ff.StackNode)}");
                    else if (e is EX_VirtualFunction vf) Console.WriteLine($"@{o} VIRT {Nm(vf.VirtualFunctionName)}");
                }
            }
        }
        return 0;
    }

    static void WalkStr(PropertyData p, Action<StrPropertyData> onStr)
    {
        switch (p)
        {
            case null: return;
            case StrPropertyData s: onStr(s); return;
            case StructPropertyData st: if (st.Value != null) foreach (var c in st.Value) WalkStr(c, onStr); return;
            case ArrayPropertyData a: if (a.Value != null) foreach (var c in a.Value) WalkStr(c, onStr); return;
            case MapPropertyData m: if (m.Value != null) foreach (var kv in m.Value) { WalkStr(kv.Key, onStr); WalkStr(kv.Value, onStr); } return;
        }
    }

    static int DumpStr(string inputDir, string outJson)
    {
        var entries = new List<Entry>();
        foreach (var path in UAssets(inputDir).OrderBy(x => x))
        {
            string rel = Path.GetRelativePath(inputDir, path).Replace('\\', '/');
            UAsset asset; try { asset = Load(path); } catch { continue; }
            foreach (var exp in asset.Exports)
                if (exp is NormalExport ne && ne.Data != null)
                    foreach (var p in ne.Data)
                        WalkStr(p, s => { if (!string.IsNullOrEmpty(Fs(s.Value))) entries.Add(new Entry { File = rel, Export = Nm(exp.ObjectName), Prop = Nm(s.Name), Source = Fs(s.Value) }); });
        }
        File.WriteAllText(outJson, JsonConvert.SerializeObject(entries, Formatting.Indented), new UTF8Encoding(false));
        Console.WriteLine($"StrProperty total: {entries.Count}");
        return 0;
    }

    static int DumpBC(string inputDir, string outJson)
    {
        var entries = new List<BcEntry>();
        int files = 0;
        foreach (var path in UAssets(inputDir).OrderBy(x => x))
        {
            files++;
            string rel = Path.GetRelativePath(inputDir, path).Replace('\\', '/');
            UAsset asset;
            try { asset = Load(path); }
            catch (Exception e) { Console.Error.WriteLine($"[skip] {rel}: {e.Message}"); continue; }
            CollectTextConsts(asset, tc => { var be = Describe(tc); be.File = rel; entries.Add(be); });
        }
        File.WriteAllText(outJson, JsonConvert.SerializeObject(entries, Formatting.Indented), new UTF8Encoding(false));
        var byType = entries.GroupBy(e => e.Type).ToDictionary(g => g.Key, g => g.Count());
        var uniq = entries.Where(e => !string.IsNullOrWhiteSpace(e.Source)).Select(e => e.Source).Distinct().ToList();
        Console.WriteLine($"files scanned    : {files}");
        Console.WriteLine($"EX_TextConst tot : {entries.Count}");
        Console.WriteLine($"by literal type  : {string.Join(", ", byType.Select(kv => $"{kv.Key}={kv.Value}"))}");
        Console.WriteLine($"unique source    : {uniq.Count}");
        return 0;
    }

    // ---- stats (diagnostic) --------------------------------------------
    static void WalkAll(PropertyData p, Action<PropertyData> visit)
    {
        if (p == null) return;
        visit(p);
        switch (p)
        {
            case StructPropertyData s: if (s.Value != null) foreach (var c in s.Value) WalkAll(c, visit); break;
            case ArrayPropertyData a: if (a.Value != null) foreach (var c in a.Value) WalkAll(c, visit); break;
            case MapPropertyData m: if (m.Value != null) foreach (var kv in m.Value) { WalkAll(kv.Key, visit); WalkAll(kv.Value, visit); } break;
        }
    }

    static int Stats(string inputDir)
    {
        var exportTypes = new Dictionary<string, int>();
        var propTypes = new Dictionary<string, int>();
        int assets = 0;
        foreach (var path in UAssets(inputDir).OrderBy(x => x))
        {
            string rel = Path.GetRelativePath(inputDir, path).Replace('\\', '/');
            UAsset asset;
            try { asset = Load(path); }
            catch (Exception e) { Console.Error.WriteLine($"[skip] {rel}: {e.Message}"); continue; }
            assets++;
            foreach (var exp in asset.Exports)
            {
                string et = exp.GetType().Name;
                exportTypes[et] = exportTypes.GetValueOrDefault(et) + 1;
                if (exp is NormalExport ne && ne.Data != null)
                    foreach (var p in ne.Data)
                        WalkAll(p, pd => { string pt = pd.GetType().Name; propTypes[pt] = propTypes.GetValueOrDefault(pt) + 1; });
            }
        }
        Console.WriteLine($"assets parsed: {assets}");
        Console.WriteLine("== export types ==");
        foreach (var kv in exportTypes.OrderByDescending(x => x.Value)) Console.WriteLine($"  {kv.Value,6}  {kv.Key}");
        Console.WriteLine("== property types ==");
        foreach (var kv in propTypes.OrderByDescending(x => x.Value)) Console.WriteLine($"  {kv.Value,6}  {kv.Key}");
        return 0;
    }

    // ---- dump -----------------------------------------------------------
    static int Dump(string inputDir, string outJson)
    {
        var entries = new List<Entry>();
        int files = 0;
        foreach (var path in UAssets(inputDir).OrderBy(x => x))
        {
            files++;
            string rel = Path.GetRelativePath(inputDir, path).Replace('\\', '/');
            UAsset asset;
            try { asset = Load(path); }
            catch (Exception e) { Console.Error.WriteLine($"[skip] {rel}: {e.Message}"); continue; }

            ForEachText(asset, (exp, t) =>
            {
                entries.Add(new Entry
                {
                    File = rel,
                    Export = Nm(exp.ObjectName),
                    Prop = Nm(t.Name),
                    History = t.HistoryType.ToString(),
                    Namespace = Fs(t.Namespace),
                    Key = Fs(t.Value),
                    Source = Fs(t.CultureInvariantString),
                });
            });
        }

        File.WriteAllText(outJson, JsonConvert.SerializeObject(entries, Formatting.Indented), new UTF8Encoding(false));

        var uniq = entries.Where(e => !string.IsNullOrWhiteSpace(e.Source))
                          .Select(e => e.Source).Distinct().OrderBy(x => x).ToList();
        Console.WriteLine($"files scanned : {files}");
        Console.WriteLine($"FText total   : {entries.Count}");
        Console.WriteLine($"unique source : {uniq.Count}");
        Console.WriteLine($"history types : {string.Join(", ", entries.Select(e => e.History).Distinct().OrderBy(x => x))}");
        return 0;
    }

    // ---- apply ----------------------------------------------------------
    // Edit FText literals inside Blueprint bytecode, fixing jump/skip offsets via a positional
    // delta model (absolute offsets shift by inserted bytes before them; EX_Context/EX_Skip
    // relative sizes are recomputed). Returns number of edits.
    // Collect (getter,setter) for every absolute code-offset field in an expression.
    static IEnumerable<(Func<uint> get, Action<uint> set)> AbsOffsets(KismetExpression e)
    {
        switch (e)
        {
            case EX_Jump j: yield return (() => j.CodeOffset, v => j.CodeOffset = v); break;
            case EX_JumpIfNot j: yield return (() => j.CodeOffset, v => j.CodeOffset = v); break;
            case EX_PushExecutionFlow p: yield return (() => p.PushingAddress, v => p.PushingAddress = v); break;
            case EX_SwitchValue sw:
                yield return (() => sw.EndGotoOffset, v => sw.EndGotoOffset = v);
                for (int i = 0; i < sw.Cases.Length; i++) { int ii = i; yield return (() => sw.Cases[ii].NextOffset, v => sw.Cases[ii].NextOffset = v); }
                break;
        }
    }

    static int bcSkippedFns = 0;

    static int bcEntryFail = 0;

    // Recursively walk a Kismet expression tree, letting `repl` replace any string-const child
    // (EX_StringConst / EX_UnicodeStringConst) in place — needed because a bare EX_StringConst can
    // only become Chinese by swapping it for an EX_UnicodeStringConst, which requires editing the
    // parent's reference. Handles EX_TextConst (payload in .Value, an FScriptText) and
    // EX_SwitchValue (Cases is a struct array) specially; everything else via reflection over
    // KismetExpression / KismetExpression[] fields.
    // `repl` applies to bare string consts (restricted to a safe whitelist); `replFText` applies
    // inside an FText literal's FScriptText subtree (full map — those are always display text).
    static void ReplaceIn(object node, Func<KismetExpression, KismetExpression> repl, Func<KismetExpression, KismetExpression> replFText)
    {
        if (node == null) return;
        switch (node)
        {
            case EX_TextConst tc: ReplaceIn(tc.Value, replFText, replFText); return;
            case EX_SwitchValue sw:
                {
                    if (sw.IndexTerm != null) { var r = repl(sw.IndexTerm); if (r != null) sw.IndexTerm = r; else ReplaceIn(sw.IndexTerm, repl, replFText); }
                    if (sw.Cases != null)
                        for (int i = 0; i < sw.Cases.Length; i++)
                        {
                            var civ = sw.Cases[i].CaseIndexValueTerm; var r1 = repl(civ); if (r1 != null) sw.Cases[i].CaseIndexValueTerm = r1; else ReplaceIn(civ, repl, replFText);
                            var ct = sw.Cases[i].CaseTerm; var r2 = repl(ct); if (r2 != null) sw.Cases[i].CaseTerm = r2; else ReplaceIn(ct, repl, replFText);
                        }
                    if (sw.DefaultTerm != null) { var r = repl(sw.DefaultTerm); if (r != null) sw.DefaultTerm = r; else ReplaceIn(sw.DefaultTerm, repl, replFText); }
                    return;
                }
        }
        foreach (var f in node.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            object val = f.GetValue(node);
            if (val is KismetExpression ke)
            {
                var r = repl(ke); if (r != null) f.SetValue(node, r); else ReplaceIn(ke, repl, replFText);
            }
            else if (val is KismetExpression[] arr)
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i] == null) continue;
                    var r = repl(arr[i]); if (r != null) arr[i] = r; else ReplaceIn(arr[i], repl, replFText);
                }
            }
            else if (val is FScriptText fst) ReplaceIn(fst, replFText, replFText);
        }
    }

    // Bare string consts are only translated for these known, unambiguous display strings (a bare
    // EX_StringConst can also be a logic key like "All"/"Play", which must NOT be translated).
    static readonly HashSet<string> BareSafe = new HashSet<string> { "Showing scenarios: " };

    // Positional-delta shift: an absolute code offset moves forward by the number of bytes inserted
    // before it. Correct for linear insertion (verified: VisitAll total == stored ScriptBytecodeSize,
    // and this matches expression-identity remapping on all aligned offsets). Handles offsets that
    // don't align to an expression start (EX_SwitchValue.EndGotoOffset) which identity-mapping can't.
    static uint ShiftBy(uint v, List<(uint pos, int delta)> deltas)
        => (uint)((int)v + deltas.Where(d => d.pos < v).Sum(d => d.delta));

    // Edit FText literals inside Blueprint bytecode.
    // PASS 1: swap each source string to Chinese (EX_UnicodeStringConst); shift every absolute jump
    //   offset by the bytes inserted before it; recompute EX_Context/EX_Skip relative sizes from the
    //   edited subtree. Record per-function insertion deltas.
    // PASS 2: cross-function fix — every caller of an edited ExecuteUbergraph_X passes a hardcoded
    //   EntryPoint offset (EX_IntConst); shift it by that ubergraph's deltas.
    static int ApplyBytecode(UAsset asset, Dictionary<string, string> map, HashSet<string> used)
    {
        int changes = 0;
        var fnDeltas = new Dictionary<string, List<(uint pos, int delta)>>();

        foreach (var exp in asset.Exports)
        {
            if (exp is not StructExport se || se.ScriptBytecode == null) continue;
            var (all, total) = VisitAll(asset, se.ScriptBytecode);
            var preOff = new Dictionary<KismetExpression, uint>();
            foreach (var (e, o) in all) preOff[e] = o;

            // find & replace every matching string const in the tree: bare EX_StringConst (e.g. a
            // menu label passed to a function) AND FText source strings (inside EX_TextConst). Both
            // become EX_UnicodeStringConst so they can hold Chinese; deltas track size changes.
            var deltas = new List<(uint pos, int delta)>();
            KismetExpression Do(KismetExpression e, bool bare)
            {
                string cur = ExprStr(e);
                if (cur == null) return null;
                if (bare && !BareSafe.Contains(cur)) return null;
                if (!map.TryGetValue(cur, out var zh) || string.IsNullOrEmpty(zh) || zh == cur) return null;
                if (!preOff.TryGetValue(e, out uint pos)) return null;
                var neu = new EX_UnicodeStringConst { Value = zh };
                deltas.Add((pos, (int)neu.GetSize(asset) - (int)e.GetSize(asset)));
                used.Add(cur);
                return neu;
            }
            Func<KismetExpression, KismetExpression> replBare = e => Do(e, true);
            Func<KismetExpression, KismetExpression> replFText = e => Do(e, false);
            for (int i = 0; i < se.ScriptBytecode.Length; i++)
            {
                var r = replBare(se.ScriptBytecode[i]);
                if (r != null) se.ScriptBytecode[i] = r; else ReplaceIn(se.ScriptBytecode[i], replBare, replFText);
            }
            if (deltas.Count == 0) continue;
            changes += deltas.Count;

            // shift every absolute jump offset positionally
            foreach (var (e, o) in all)
                foreach (var (get, set) in AbsOffsets(e))
                    set(ShiftBy(get(), deltas));

            // recompute relative sizes from the edited subtrees
            foreach (var (e, o) in all)
            {
                switch (e)
                {
                    case EX_Context c: c.Offset = c.ContextExpression.GetSize(asset); break;
                    case EX_Skip s: s.CodeOffset = s.SkipExpression.GetSize(asset); break;
                }
            }

            fnDeltas[Nm(exp.ObjectName)] = deltas;
        }

        // PASS 2: shift hardcoded ExecuteUbergraph entry-point offsets in every caller
        if (fnDeltas.Count > 0)
        {
            foreach (var exp in asset.Exports)
            {
                if (exp is not StructExport se || se.ScriptBytecode == null) continue;
                var (all, _) = VisitAll(asset, se.ScriptBytecode);
                foreach (var (e, o) in all)
                {
                    if (e is EX_FinalFunction ff && ff.Parameters != null && ff.Parameters.Length > 0 && ff.Parameters[0] is EX_IntConst ic)
                    {
                        string tgt = ResolveIdx(asset, ff.StackNode);
                        if (tgt != null && fnDeltas.TryGetValue(tgt, out var dl))
                            ic.Value = (int)ShiftBy((uint)ic.Value, dl);
                    }
                }
            }
        }
        return changes;
    }

    // Post-edit consistency: modelable jump targets must align to expression starts and
    // EX_Context.Offset must equal the size of its ContextExpression. (EX_SwitchValue.EndGotoOffset
    // is not checked — UAssetAPI's in-memory accounting for it is off by a constant, but the
    // positional-delta fixup shifts it correctly regardless.)
    static int VerifyBytecode(UAsset asset)
    {
        int bad = 0;
        // statement-start offsets per function (for entry-point checking)
        var startsByFn = new Dictionary<string, HashSet<uint>>();
        var totalByFn = new Dictionary<string, uint>();
        foreach (var exp in asset.Exports)
            if (exp is StructExport se0 && se0.ScriptBytecode != null)
            {
                var (a0, t0) = VisitAll(asset, se0.ScriptBytecode);
                var s0 = new HashSet<uint>(); foreach (var (e, o) in a0) s0.Add(o);
                startsByFn[Nm(exp.ObjectName)] = s0; totalByFn[Nm(exp.ObjectName)] = t0;
            }

        foreach (var exp in asset.Exports)
        {
            if (exp is not StructExport se || se.ScriptBytecode == null) continue;
            var (all, total) = VisitAll(asset, se.ScriptBytecode);
            var starts = startsByFn[Nm(exp.ObjectName)];
            bool Ok(uint t) => t == total || starts.Contains(t);
            foreach (var (e, o) in all)
            {
                switch (e)
                {
                    case EX_Jump j: if (!Ok(j.CodeOffset)) bad++; break;
                    case EX_JumpIfNot j: if (!Ok(j.CodeOffset)) bad++; break;
                    case EX_PushExecutionFlow p: if (!Ok(p.PushingAddress)) bad++; break;
                    case EX_Context c: if (c.Offset != c.ContextExpression.GetSize(asset)) bad++; break;
                    case EX_Skip s: if (s.CodeOffset != s.SkipExpression.GetSize(asset)) bad++; break;
                    case EX_FinalFunction ff when ff.Parameters != null && ff.Parameters.Length > 0 && ff.Parameters[0] is EX_IntConst ic:
                        {
                            string tgt = ResolveIdx(asset, ff.StackNode);
                            if (tgt != null && tgt.Contains("ExecuteUbergraph") && startsByFn.TryGetValue(tgt, out var us))
                            {
                                uint ep = (uint)ic.Value;
                                if (ep != totalByFn[tgt] && !us.Contains(ep)) bad++;
                            }
                        }
                        break;
                }
            }
        }
        return bad;
    }

    static int Apply(string inputDir, string mapJson, string outputDir)
    {
        var map = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(mapJson))
                  ?? new Dictionary<string, string>();
        int changedFiles = 0, changedTexts = 0, matchedKeys = 0, verifyFail = 0;
        var used = new HashSet<string>();

        foreach (var path in UAssets(inputDir).OrderBy(x => x))
        {
            string rel = Path.GetRelativePath(inputDir, path).Replace('\\', '/');
            UAsset asset;
            try { asset = Load(path); }
            catch (Exception e) { Console.Error.WriteLine($"[skip] {rel}: {e.Message}"); continue; }

            int propChanges = 0;
            ForEachText(asset, (exp, t) =>
            {
                string cur = Fs(t.CultureInvariantString);
                if (cur != null && map.TryGetValue(cur, out var zh) && !string.IsNullOrEmpty(zh) && zh != cur)
                {
                    t.CultureInvariantString = new FString(zh, Encoding.Unicode);
                    propChanges++;
                    used.Add(cur);
                }
            });
            int strChanges = 0;
            foreach (var exp in asset.Exports)
                if (exp is NormalExport ne && ne.Data != null)
                    foreach (var p in ne.Data)
                        WalkStr(p, s =>
                        {
                            string cur = Fs(s.Value);
                            if (cur != null && map.TryGetValue(cur, out var zh) && !string.IsNullOrEmpty(zh) && zh != cur)
                            {
                                s.Value = new FString(zh, Encoding.Unicode);
                                strChanges++; used.Add(cur);
                            }
                        });
            int bcChanges = ApplyBytecode(asset, map, used);
            int localChanges = propChanges + strChanges + bcChanges;

            if (localChanges > 0)
            {
                // safety 1: in-memory bytecode consistency after fixup
                int bcBad = VerifyBytecode(asset);
                if (bcBad > 0)
                {
                    Console.Error.WriteLine($"[VERIFY-FAIL] {rel}: {bcBad} inconsistent bytecode offsets after fixup -- skipping asset");
                    verifyFail++;
                    continue;
                }

                string outPath = Path.Combine(outputDir, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                asset.Write(outPath);

                // safety 2: re-parse what we just wrote; if it won't load or bytecode is
                // inconsistent, discard this asset.
                try { var rt = new UAsset(outPath, EV, Maps); if (VerifyBytecode(rt) > 0) throw new Exception("post-write bytecode inconsistent"); }
                catch (Exception ve)
                {
                    Console.Error.WriteLine($"[VERIFY-FAIL] {rel}: {ve.Message} -- reverting this asset");
                    File.Delete(outPath);
                    string uexp = Path.ChangeExtension(outPath, ".uexp");
                    if (File.Exists(uexp)) File.Delete(uexp);
                    verifyFail++;
                    continue;
                }
                changedFiles++;
                changedTexts += localChanges;
                Console.WriteLine($"[write] {rel}  (prop={propChanges} str={strChanges} bc={bcChanges})");
            }
        }

        matchedKeys = used.Count;
        var unused = map.Keys.Where(k => !used.Contains(k) && !string.IsNullOrEmpty(map[k])).ToList();
        Console.WriteLine($"changed files : {changedFiles}");
        Console.WriteLine($"changed texts : {changedTexts}");
        Console.WriteLine($"verify-fail   : {verifyFail}");
        Console.WriteLine($"map keys used : {matchedKeys}/{map.Count}");
        if (unused.Count > 0)
            Console.WriteLine("UNUSED map keys (no FText matched):\n  - " + string.Join("\n  - ", unused));
        return 0;
    }
}

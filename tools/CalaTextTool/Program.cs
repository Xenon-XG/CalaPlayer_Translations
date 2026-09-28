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
using UAssetAPI.FieldTypes;
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
            if (args.Length >= 3 && args[0] == "disasm") { Maps = TryLoadUsmap(args.Length >= 4 ? args[3] : null); return Disasm(args[1], args[2]); }
            if (args.Length >= 3 && args[0] == "injectlabel") { Maps = TryLoadUsmap(args.Length >= 4 ? args[3] : null); return InjectLabel(args[1], args[2]); }
            if (args.Length >= 3 && args[0] == "names") { Maps = TryLoadUsmap(args.Length >= 4 ? args[3] : null); foreach (var pth in UAssets(args[1])) if (Path.GetFileName(pth).Contains(args[2])) { var a = Load(pth); foreach (var n in a.GetNameMapIndexList()) Console.WriteLine(n.Value); } return 0; }
            if (args.Length >= 3 && args[0] == "props") { Maps = TryLoadUsmap(args.Length >= 4 ? args[3] : null); return Props(args[1], args[2]); }
            if (args.Length >= 3 && args[0] == "exports") { Maps = TryLoadUsmap(args.Length >= 4 ? args[3] : null); return Exports(args[1], args[2]); }
            if (args.Length >= 3 && args[0] == "imports") { Maps = TryLoadUsmap(args.Length >= 4 ? args[3] : null); return Imports(args[1], args[2]); }
            if (args.Length >= 3 && args[0] == "injectmap") { Maps = TryLoadUsmap(args.Length >= 4 ? args[3] : null); return InjectMap3(args[1], args[2]); }
            if (args.Length >= 5 && args[0] == "injectrender") { Maps = TryLoadUsmap(args.Length >= 6 ? args[5] : null); return InjectRender(args[1], args[2], args[3], args[4], args.Length >= 7 ? args[6] : null); }
            if (args.Length >= 8 && args[0] == "injectvarmap") { Maps = TryLoadUsmap(args.Length >= 9 ? args[8] : null); return InjectVarMap(args[1], args[2], args[3], args[4], args[5], args[6], args[7]); }
            if (args.Length >= 4 && args[0] == "injectsetprop") { Maps = TryLoadUsmap(args.Length >= 5 ? args[4] : null); return InjectSetProp(args[1], args[2], args[3], args.Length >= 6 ? args[5] : null); }
            if (args.Length >= 4 && args[0] == "injectdiag") { Maps = TryLoadUsmap(args.Length >= 5 ? args[4] : null); return InjectDiag(args[1], args[2], args[3]); }
            if (args.Length >= 5 && args[0] == "injectconcat") { Maps = TryLoadUsmap(args.Length >= 6 ? args[5] : null); return InjectConcat(args[1], args[2], args[3], args[4]); }
            if (args.Length >= 3 && args[0] == "injectswtest") { Maps = TryLoadUsmap(args.Length >= 4 ? args[3] : null); return InjectSwTest(args[1], args[2]); }
            if (args.Length >= 2 && args[0] == "injectimportonly") { Maps = TryLoadUsmap(args.Length >= 3 ? args[2] : null); return InjectImportOnly(args[1]); }
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
                        uint sz = sw.GetSize(asset);
                        uint defOff = all.Where(x => ReferenceEquals(x.e, sw.DefaultTerm)).Select(x => x.o).FirstOrDefault();
                        Console.WriteLine($"  SWITCH @{o} size={sz} o+size={o + sz} EndGoto={sw.EndGotoOffset} DefaultTermOff={defOff}");
                        for (int ci = 0; ci < sw.Cases.Length; ci++)
                        {
                            uint civOff = all.Where(x => ReferenceEquals(x.e, sw.Cases[ci].CaseIndexValueTerm)).Select(x => x.o).FirstOrDefault();
                            uint ctOff = all.Where(x => ReferenceEquals(x.e, sw.Cases[ci].CaseTerm)).Select(x => x.o).FirstOrDefault();
                            Console.WriteLine($"    case[{ci}] valOff={civOff} NextOffset={sw.Cases[ci].NextOffset} termOff={ctOff}");
                        }
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

    // ---- readable bytecode disassembly (diagnostic) --------------------
    static string PtrName(object ptr)
    {
        if (ptr == null) return "<null-ptr>";
        try
        {
            var t = ptr.GetType();
            var neu = t.GetField("New")?.GetValue(ptr) ?? t.GetProperty("New")?.GetValue(ptr);
            if (neu != null)
            {
                var path = neu.GetType().GetField("Path")?.GetValue(neu) as System.Collections.IEnumerable;
                if (path != null)
                {
                    var parts = new List<string>();
                    foreach (var p in path) parts.Add(p?.ToString());
                    if (parts.Count > 0) return string.Join(".", parts);
                }
            }
            var old = t.GetField("Old")?.GetValue(ptr) ?? t.GetProperty("Old")?.GetValue(ptr);
            if (old is FPackageIndex fp) return "idx:" + fp.Index;
        }
        catch { }
        return "<ptr?>";
    }

    static void PrintExpr(UAsset asset, KismetExpression e, int depth, string label)
    {
        string ind = new string(' ', depth * 2);
        var extra = new List<string>();
        switch (e)
        {
            case EX_IntConst i: extra.Add("=" + i.Value); break;
            case EX_StringConst s: extra.Add("\"" + s.Value + "\""); break;
            case EX_UnicodeStringConst u: extra.Add("u\"" + u.Value + "\""); break;
            case EX_NameConst n: extra.Add("name=" + Nm(n.Value)); break;
            case EX_ByteConst b: extra.Add("=" + b.Value); break;
            case EX_TextConst tc: extra.Add("text[" + tc.Value.TextLiteralType + "]"); break;
            case EX_ObjectConst oc: extra.Add("obj=" + ResolveIdx(asset, oc.Value)); break;
            case EX_CallMath cm: extra.Add("-> " + ResolveIdx(asset, cm.StackNode)); break;
            case EX_FinalFunction ff: extra.Add("-> " + ResolveIdx(asset, ff.StackNode)); break;
            case EX_VirtualFunction vf: extra.Add("-> " + Nm(vf.VirtualFunctionName)); break;
            case EX_LocalVariable lv: extra.Add(PtrName(lv.Variable)); break;
            case EX_InstanceVariable iv: extra.Add(PtrName(iv.Variable)); break;
            case EX_LocalOutVariable ov: extra.Add(PtrName(ov.Variable)); break;
            case EX_Context ctx: extra.Add("off=" + ctx.Offset); break;
            case EX_JumpIfNot j: extra.Add("->@" + j.CodeOffset); break;
            case EX_Jump j: extra.Add("->@" + j.CodeOffset); break;
        }
        Console.WriteLine($"{ind}{(label != null ? label + ": " : "")}{e.GetType().Name}{(extra.Count > 0 ? " " + string.Join(" ", extra) : "")}");
        // recurse into child expressions via reflection
        var ty = e.GetType();
        foreach (var f in ty.GetFields(BindingFlags.Public | BindingFlags.Instance))
            EmitChild(asset, f.Name, f.GetValue(e), depth + 1);
        foreach (var p in ty.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length != 0) continue;
            object v; try { v = p.GetValue(e); } catch { continue; }
            EmitChild(asset, p.Name, v, depth + 1);
        }
    }

    static void EmitChild(UAsset asset, string label, object v, int depth)
    {
        if (v is KismetExpression ke) { PrintExpr(asset, ke, depth, label); return; }
        if (v is KismetExpression[] arr) { for (int i = 0; i < arr.Length; i++) if (arr[i] != null) PrintExpr(asset, arr[i], depth, $"{label}[{i}]"); return; }
        if (v is string || v is byte[]) return;
        if (v is System.Collections.IEnumerable en)
        {
            foreach (var it in en)
            {
                if (it is KismetExpression k2) { PrintExpr(asset, k2, depth, label); continue; }
                var tt = it?.GetType();
                if (tt != null && tt.Name.Contains("SwitchCase"))
                {
                    var civ = tt.GetField("CaseIndexValueTerm")?.GetValue(it) as KismetExpression;
                    var ct = tt.GetField("CaseTerm")?.GetValue(it) as KismetExpression;
                    if (civ != null) PrintExpr(asset, civ, depth, label + ".caseVal");
                    if (ct != null) PrintExpr(asset, ct, depth, label + ".caseTerm");
                }
            }
        }
    }

    // List exports whose class name contains <classFilter> (e.g. "MorphTarget"): one name per line.
    static int Exports(string inputDir, string classFilter)
    {
        foreach (var path in UAssets(inputDir).OrderBy(x => x))
        {
            UAsset asset; try { asset = Load(path); } catch (Exception e) { Console.Error.WriteLine($"[skip] {Path.GetFileName(path)}: {e.Message}"); continue; }
            foreach (var exp in asset.Exports)
            {
                string cls = ResolveIdx(asset, exp.ClassIndex) ?? "";
                if (classFilter.Length == 0 || cls.Contains(classFilter))
                    Console.WriteLine($"{Path.GetFileNameWithoutExtension(path)}\t{cls}\t{exp.ObjectName}");
            }
        }
        return 0;
    }

    static int Imports(string inputDir, string fileSub)
    {
        foreach (var path in UAssets(inputDir).OrderBy(x => x))
        {
            if (fileSub.Length > 0 && !Path.GetFileName(path).Contains(fileSub)) continue;
            UAsset asset; try { asset = Load(path); } catch { continue; }
            Console.WriteLine($"==== {Path.GetFileName(path)}  imports={asset.Imports.Count} ====");
            for (int i = 0; i < asset.Imports.Count; i++)
            {
                var im = asset.Imports[i];
                Console.WriteLine($"  [-{i + 1}] pkg={Nm(im.ClassPackage)} cls={Nm(im.ClassName)} name={Nm(im.ObjectName)} outer={im.OuterIndex?.Index}");
            }
        }
        return 0;
    }

    static int Disasm(string inputDir, string fnFilter)
    {
        foreach (var path in UAssets(inputDir).OrderBy(x => x))
        {
            UAsset asset;
            try { asset = Load(path); } catch { continue; }
            foreach (var exp in asset.Exports)
            {
                if (exp is not StructExport se || se.ScriptBytecode == null) continue;
                string fn = Nm(exp.ObjectName) ?? "";
                if (fnFilter.Length > 0 && !fn.Contains(fnFilter)) continue;
                var (all, total) = VisitAll(asset, se.ScriptBytecode);
                Console.WriteLine($"\n==== {Path.GetFileName(path)} :: {fn}  (stmts={se.ScriptBytecode.Length} size={total}) ====");
                uint off = 0;
                foreach (var top in se.ScriptBytecode)
                {
                    Console.WriteLine($"-- @{off} --");
                    PrintExpr(asset, top, 0, null);
                    off += top.GetSize(asset);
                }
            }
        }
        return 0;
    }

    // ---- PoC: inject a display label transform into WBP_DropdownButtonContent ----
    // Replaces the argument of TextBlock.SetText(In Text) with a test Chinese constant, to prove
    // the label is editable from this one widget and that selection/value are unaffected.
    static int InjectLabel(string inputDir, string testText)
    {
        string target = null;
        foreach (var p in UAssets(inputDir))
            if (Path.GetFileName(p) == "WBP_DropdownButtonContent.uasset") { target = p; break; }
        if (target == null) { Console.Error.WriteLine("找不到 WBP_DropdownButtonContent.uasset"); return 2; }
        var asset = Load(target);
        var fnDeltas = new Dictionary<string, List<(uint pos, int delta)>>();
        int edits = 0;

        foreach (var exp in asset.Exports)
        {
            if (exp is not StructExport se || se.ScriptBytecode == null) continue;
            var (all, total) = VisitAll(asset, se.ScriptBytecode);
            var preOff = new Dictionary<KismetExpression, uint>();
            foreach (var (e, o) in all) preOff[e] = o;

            var deltas = new List<(uint pos, int delta)>();
            foreach (var (e, o) in all)
            {
                if (e is EX_VirtualFunction vf && Nm(vf.VirtualFunctionName) == "SetText"
                    && vf.Parameters != null && vf.Parameters.Length >= 1
                    && vf.Parameters[0] is EX_InstanceVariable iv && PtrName(iv.Variable).Contains("In Text"))
                {
                    var old = vf.Parameters[0];
                    if (!preOff.TryGetValue(old, out uint pos)) continue;
                    var neu = new EX_TextConst
                    {
                        Value = new FScriptText
                        {
                            TextLiteralType = EBlueprintTextLiteralType.InvariantText,
                            InvariantLiteralString = new EX_UnicodeStringConst { Value = testText }
                        }
                    };
                    vf.Parameters[0] = neu;
                    deltas.Add((pos, (int)neu.GetSize(asset) - (int)old.GetSize(asset)));
                    edits++;
                    Console.WriteLine($"  注入 @{pos}: SetText(In Text) -> SetText(\"{testText}\")  Δsize={(int)neu.GetSize(asset) - (int)old.GetSize(asset)}");
                }
            }
            if (deltas.Count == 0) continue;

            foreach (var (e, o) in all)
                foreach (var (get, set) in AbsOffsets(e))
                    set(ShiftBy(get(), deltas));
            foreach (var (e, o) in all)
                switch (e)
                {
                    case EX_Context c: c.Offset = c.ContextExpression.GetSize(asset); break;
                    case EX_Skip s: s.CodeOffset = s.SkipExpression.GetSize(asset); break;
                }
            fnDeltas[Nm(exp.ObjectName)] = deltas;
        }

        if (edits == 0) { Console.Error.WriteLine("没找到 SetText(In Text) 注入点"); return 3; }

        // pass 2: shift hardcoded ExecuteUbergraph entry offsets in callers
        foreach (var exp in asset.Exports)
        {
            if (exp is not StructExport se || se.ScriptBytecode == null) continue;
            var (all, _) = VisitAll(asset, se.ScriptBytecode);
            foreach (var (e, o) in all)
                if (e is EX_FinalFunction ff && ff.Parameters != null && ff.Parameters.Length > 0 && ff.Parameters[0] is EX_IntConst ic)
                {
                    string tgt = ResolveIdx(asset, ff.StackNode);
                    if (tgt != null && fnDeltas.TryGetValue(tgt, out var dl)) ic.Value = (int)ShiftBy((uint)ic.Value, dl);
                }
        }

        int bad = VerifyBytecode(asset);
        Console.WriteLine($"verify-fail: {bad}");
        if (bad > 0) { Console.Error.WriteLine("字节码校验不过，放弃写入"); return 4; }
        asset.Write(target);
        try { var rt = new UAsset(target, EV, Maps); if (VerifyBytecode(rt) > 0) throw new Exception("post-write inconsistent"); }
        catch (Exception ve) { Console.Error.WriteLine($"重解析校验失败: {ve.Message}"); return 5; }
        Console.WriteLine($"[ok] 注入完成并通过校验，写回 {Path.GetFileName(target)}（{edits} 处）");
        return 0;
    }

    // ---- Resource-name display map: inject a String->ZH switch into WBP_DropdownButtonContent ----
    // Replaces SetText(In Text) with SetText( Switch( Conv_TextToString(In Text) : "EN"->Text("ZH") ... default In Text ) ).
    // Display-only: the outer ComboBox's selected FName value is untouched.
    static int InjectMap(string inputDir, string mapJson)
    {
        var raw = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(mapJson)) ?? new Dictionary<string, string>();
        var map = raw.Where(kv => !string.IsNullOrEmpty(kv.Value) && kv.Value != kv.Key)
                     .GroupBy(kv => kv.Key.ToLowerInvariant()).Select(g => g.First()).ToList();   // compare is case-insensitive
        if (map.Count == 0) { Console.Error.WriteLine("map 为空"); return 2; }

        string target = UAssets(inputDir).FirstOrDefault(p => Path.GetFileName(p) == "WBP_DropdownButtonContent.uasset");
        if (target == null) { Console.Error.WriteLine("找不到 WBP_DropdownButtonContent.uasset"); return 2; }
        var asset = Load(target);

        FPackageIndex ImpIdx(string obj)
        {
            for (int i = 0; i < asset.Imports.Count; i++)
                if (Nm(asset.Imports[i].ObjectName) == obj) return FPackageIndex.FromImport(i);
            return null;
        }
        var enginePkg = ImpIdx("/Script/Engine") ?? asset.AddImport(new Import("/Script/CoreUObject", "Package", new FPackageIndex(0), "/Script/Engine", false, asset));
        var ktlClass = ImpIdx("KismetTextLibrary") ?? asset.AddImport(new Import("/Script/CoreUObject", "Class", enginePkg, "KismetTextLibrary", false, asset));
        var convFunc = ImpIdx("Conv_TextToString") ?? asset.AddImport(new Import("/Script/CoreUObject", "Object", ktlClass, "Conv_TextToString", false, asset));
        Console.WriteLine($"Conv_TextToString import = {convFunc.Index}  (cases={map.Count})");

        var fnDeltas = new Dictionary<string, List<(uint pos, int delta)>>();
        var mySwitches = new List<EX_SwitchValue>();
        int edits = 0;

        foreach (var exp in asset.Exports)
        {
            if (exp is not StructExport se || se.ScriptBytecode == null) continue;
            var (all, total) = VisitAll(asset, se.ScriptBytecode);
            var preOff = new Dictionary<KismetExpression, uint>();
            foreach (var (e, o) in all) preOff[e] = o;

            var deltas = new List<(uint pos, int delta)>();
            foreach (var (e, o) in all)
            {
                if (e is EX_VirtualFunction vf && Nm(vf.VirtualFunctionName) == "SetText"
                    && vf.Parameters != null && vf.Parameters.Length >= 1
                    && vf.Parameters[0] is EX_InstanceVariable iv && PtrName(iv.Variable).Contains("In Text"))
                {
                    var old = vf.Parameters[0];
                    if (!preOff.TryGetValue(old, out uint pos)) continue;
                    var ptr = ((EX_InstanceVariable)old).Variable;
                    EX_InstanceVariable MkIn() => new EX_InstanceVariable { Variable = ptr };
                    var idxTerm = new EX_CallMath { StackNode = convFunc, Parameters = new KismetExpression[] { MkIn() } };
                    var cases = map.Select(kv => new FKismetSwitchCase(
                        new EX_StringConst { Value = kv.Key }, 0u,
                        new EX_TextConst { Value = new FScriptText { TextLiteralType = EBlueprintTextLiteralType.InvariantText, InvariantLiteralString = new EX_UnicodeStringConst { Value = kv.Value } } }
                    )).ToArray();
                    var sw = new EX_SwitchValue { EndGotoOffset = 0, IndexTerm = idxTerm, Cases = cases, DefaultTerm = MkIn() };
                    vf.Parameters[0] = sw;
                    mySwitches.Add(sw);
                    deltas.Add((pos, (int)sw.GetSize(asset) - (int)old.GetSize(asset)));
                    edits++;
                    Console.WriteLine($"  注入 switch @{pos}  Δsize={(int)sw.GetSize(asset) - (int)old.GetSize(asset)}");
                }
            }
            if (deltas.Count == 0) continue;

            foreach (var (e, o) in all)
                foreach (var (get, set) in AbsOffsets(e))
                    set(ShiftBy(get(), deltas));
            foreach (var (e, o) in all)
                switch (e)
                {
                    case EX_Context c: c.Offset = c.ContextExpression.GetSize(asset); break;
                    case EX_Skip s: s.CodeOffset = s.SkipExpression.GetSize(asset); break;
                }
            fnDeltas[Nm(exp.ObjectName)] = deltas;
        }
        if (edits == 0) { Console.Error.WriteLine("没找到 SetText(In Text) 注入点"); return 3; }

        // pass 2: shift hardcoded ExecuteUbergraph entry offsets in callers
        foreach (var exp in asset.Exports)
        {
            if (exp is not StructExport se || se.ScriptBytecode == null) continue;
            var (all, _) = VisitAll(asset, se.ScriptBytecode);
            foreach (var (e, o) in all)
                if (e is EX_FinalFunction ff && ff.Parameters != null && ff.Parameters.Length > 0 && ff.Parameters[0] is EX_IntConst ic)
                {
                    string tgt = ResolveIdx(asset, ff.StackNode);
                    if (tgt != null && fnDeltas.TryGetValue(tgt, out var dl)) ic.Value = (int)ShiftBy((uint)ic.Value, dl);
                }
        }

        // compute the new switch offsets from final positions
        foreach (var exp in asset.Exports)
        {
            if (exp is not StructExport se || se.ScriptBytecode == null) continue;
            var (all, _) = VisitAll(asset, se.ScriptBytecode);
            var offOf = new Dictionary<KismetExpression, uint>();
            foreach (var (e, o) in all) offOf[e] = o;
            foreach (var (e, o) in all)
            {
                if (e is EX_SwitchValue sw && mySwitches.Contains(sw))
                {
                    sw.EndGotoOffset = o + sw.GetSize(asset);
                    for (int i = 0; i < sw.Cases.Length; i++)
                    {
                        KismetExpression nextStart = (i + 1 < sw.Cases.Length) ? sw.Cases[i + 1].CaseIndexValueTerm : sw.DefaultTerm;
                        sw.Cases[i].NextOffset = offOf[nextStart];
                    }
                    Console.WriteLine($"  switch @{o}: EndGoto={sw.EndGotoOffset} nexts=[{string.Join(",", sw.Cases.Select(c => c.NextOffset))}]");
                }
            }
        }

        int bad = VerifyBytecode(asset);
        Console.WriteLine($"verify-fail: {bad}");
        if (bad > 0) { Console.Error.WriteLine("校验不过，放弃"); return 4; }
        asset.Write(target);
        try { var rt = new UAsset(target, EV, Maps); if (VerifyBytecode(rt) > 0) throw new Exception("post-write inconsistent"); }
        catch (Exception ve) { Console.Error.WriteLine($"重解析校验失败: {ve.Message}"); return 5; }
        Console.WriteLine($"[ok] 条件查表注入完成，写回 {Path.GetFileName(target)}（{edits} 处，{map.Count} 条映射）");
        return 0;
    }

    // Resource-name display map via statement-level JumpIfNot (avoids the SwitchValue index-typing hang).
    // After `In Text = <incoming name>`, insert per entry:
    //   JumpIfNot( EqualEqual_StrStr( Conv_TextToString(In Text), "EN" ) ) -> <next>
    //   In Text = Text("ZH")
    // In Text is an instance var, so the reassignment carries to the later visual SetText.
    static int InjectMap2(string inputDir, string mapJson)
    {
        var raw = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(mapJson)) ?? new Dictionary<string, string>();
        var map = raw.Where(kv => !string.IsNullOrEmpty(kv.Value) && kv.Value != kv.Key)
                     .GroupBy(kv => kv.Key.ToLowerInvariant()).Select(g => g.First()).ToList();   // compare is case-insensitive
        if (map.Count == 0) { Console.Error.WriteLine("map 为空"); return 2; }
        string target = UAssets(inputDir).FirstOrDefault(p => Path.GetFileName(p) == "WBP_DropdownButtonContent.uasset");
        if (target == null) { Console.Error.WriteLine("找不到 widget"); return 2; }
        var asset = Load(target);

        FPackageIndex ImpIdx(string obj) { for (int i = 0; i < asset.Imports.Count; i++) if (Nm(asset.Imports[i].ObjectName) == obj) return FPackageIndex.FromImport(i); return null; }
        var enginePkg = ImpIdx("/Script/Engine") ?? asset.AddImport(new Import("/Script/CoreUObject", "Package", new FPackageIndex(0), "/Script/Engine", false, asset));
        FPackageIndex EnsureClass(string cls) => ImpIdx(cls) ?? asset.AddImport(new Import("/Script/CoreUObject", "Class", enginePkg, cls, false, asset));
        FPackageIndex EnsureFunc(string fn, FPackageIndex cls) => ImpIdx(fn) ?? asset.AddImport(new Import("/Script/CoreUObject", "Object", cls, fn, false, asset));
        var convIdx = EnsureFunc("Conv_TextToString", EnsureClass("KismetTextLibrary"));
        // case-INSENSITIVE: saved data can differ in case from the live option names (UE's JSON
        // serializer lowercases the first letter of map keys, e.g. "lower_Eyelid_Right", and the
        // animation data itself has variants like Lose_idle/Lose_Idle); FNames here keep their casing.
        var eqIdx = EnsureFunc("EqualEqual_StriStri", EnsureClass("KismetStringLibrary"));

        StructExport se = null; int li = -1; EX_Let origLet = null;
        foreach (var exp in asset.Exports)
        {
            if (exp is StructExport s && s.ScriptBytecode != null && (Nm(exp.ObjectName) ?? "").Contains("ExecuteUbergraph"))
            {
                for (int i = 0; i < s.ScriptBytecode.Length; i++)
                    if (s.ScriptBytecode[i] is EX_Let lt && lt.Variable is EX_InstanceVariable ivv && PtrName(ivv.Variable).Contains("In Text"))
                    { se = s; li = i; origLet = lt; break; }
                if (se != null) break;
            }
        }
        if (se == null) { Console.Error.WriteLine("找不到 In Text 赋值语句"); return 3; }
        var inTextPtr = ((EX_InstanceVariable)origLet.Variable).Variable;
        var letValue = origLet.Value;

        var (allPre, total) = VisitAll(asset, se.ScriptBytecode);
        var offPre = new Dictionary<KismetExpression, uint>(); foreach (var (e, o) in allPre) offPre[e] = o;
        var origNext = se.ScriptBytecode[li + 1];
        uint P = offPre[origNext];

        EX_InstanceVariable MkIn() => new EX_InstanceVariable { Variable = inTextPtr };
        var newStmts = new List<KismetExpression>();
        var jins = new List<EX_JumpIfNot>();
        foreach (var kv in map)
        {
            var conv = new EX_CallMath { StackNode = convIdx, Parameters = new KismetExpression[] { MkIn() } };
            var cmp = new EX_CallMath { StackNode = eqIdx, Parameters = new KismetExpression[] { conv, new EX_StringConst { Value = kv.Key } } };
            var jin = new EX_JumpIfNot { CodeOffset = 0, BooleanExpression = cmp };
            var let = new EX_Let { Value = letValue, Variable = MkIn(), Expression = new EX_TextConst { Value = new FScriptText { TextLiteralType = EBlueprintTextLiteralType.InvariantText, InvariantLiteralString = new EX_UnicodeStringConst { Value = kv.Value } } } };
            newStmts.Add(jin); newStmts.Add(let); jins.Add(jin);
        }
        var listStmts = se.ScriptBytecode.ToList();
        listStmts.InsertRange(li + 1, newStmts);
        se.ScriptBytecode = listStmts.ToArray();

        int D = newStmts.Sum(s => (int)s.GetSize(asset));
        var deltas = new List<(uint pos, int delta)> { (P == 0 ? 0u : P - 1, D) };
        foreach (var (e, o) in allPre) foreach (var (get, set) in AbsOffsets(e)) set(ShiftBy(get(), deltas));
        var fnDeltas = new Dictionary<string, List<(uint pos, int delta)>> { { Nm(se.ObjectName) ?? "", deltas } };

        foreach (var exp in asset.Exports)
        {
            if (exp is not StructExport s2 || s2.ScriptBytecode == null) continue;
            var (all2, _) = VisitAll(asset, s2.ScriptBytecode);
            foreach (var (e, o) in all2) if (e is EX_FinalFunction ff && ff.Parameters != null && ff.Parameters.Length > 0 && ff.Parameters[0] is EX_IntConst ic)
            { string tgt = ResolveIdx(asset, ff.StackNode); if (tgt != null && fnDeltas.TryGetValue(tgt, out var dl)) ic.Value = (int)ShiftBy((uint)ic.Value, dl); }
        }

        var (allPost, _) = VisitAll(asset, se.ScriptBytecode);
        var offPost = new Dictionary<KismetExpression, uint>(); foreach (var (e, o) in allPost) offPost[e] = o;
        for (int i = 0; i < jins.Count; i++)
        {
            KismetExpression tgt = (i + 1 < jins.Count) ? (KismetExpression)jins[i + 1] : origNext;
            jins[i].CodeOffset = offPost[tgt];
        }

        int bad = VerifyBytecode(asset);
        Console.WriteLine($"insert D={D} P={P} entries={map.Count} verify-fail={bad}");
        if (bad > 0) { Console.Error.WriteLine("校验不过"); return 4; }
        asset.Write(target);
        try { var rt = new UAsset(target, EV, Maps); if (VerifyBytecode(rt) > 0) throw new Exception("post-write inconsistent"); }
        catch (Exception ve) { Console.Error.WriteLine("重解析失败: " + ve.Message); return 5; }
        Console.WriteLine($"[ok] JumpIfNot 查表注入完成（{map.Count} 条映射）");
        return 0;
    }

    // Resource-name display map v3: translate ONLY the visual SetText render, leave In Text untouched
    // (the consumer reads In Text as the functional key, so In Text must stay English).
    // Before the `TextBlock.SetText(In Text)` statement, per entry insert:
    //   JumpIfNot( EqualEqual_StrStr(Conv_TextToString(In Text),"EN") ) -> next
    //   TextBlock.SetText( Text("ZH") )      // a cloned context, different literal
    //   Jump -> afterOriginalSetText
    // On no match, control falls through to the original SetText(In Text).
    // Clone a variable-read expression (instance/local/default) reusing its property pointer.
    static KismetExpression CloneVarRead(KismetExpression e)
    {
        // struct member of a variable (e.g. TextLine.CharacterName): a pure read, safe to re-evaluate
        if (e is EX_StructMemberContext sm)
        {
            var inner = CloneVarRead(sm.StructExpression);
            return inner == null ? null : new EX_StructMemberContext { StructMemberExpression = sm.StructMemberExpression, StructExpression = inner };
        }
        var ptr = (e as EX_VariableBase)?.Variable;
        if (ptr == null) return null;
        return e switch
        {
            EX_InstanceVariable => new EX_InstanceVariable { Variable = ptr },
            EX_LocalVariable => new EX_LocalVariable { Variable = ptr },
            EX_LocalOutVariable => new EX_LocalOutVariable { Variable = ptr },
            _ => new EX_LocalVariable { Variable = ptr }
        };
    }

    static int InjectMap3(string inputDir, string mapJson) => InjectRender(inputDir, mapJson, "WBP_DropdownButtonContent.uasset", "TextBlock_25");

    // Generalized render-only translation: at `<tbFilter>.SetText(<var>)`, branch to SetText(Text("ZH"))
    // for mapped names, leaving the underlying variable (the functional key) untouched.
    static int InjectRender(string inputDir, string mapJson, string fileName, string tbFilter, string argFilter = null)
    {
        var raw = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(mapJson)) ?? new Dictionary<string, string>();
        var map = raw.Where(kv => !string.IsNullOrEmpty(kv.Value) && kv.Value != kv.Key)
                     .GroupBy(kv => kv.Key.ToLowerInvariant()).Select(g => g.First()).ToList();   // compare is case-insensitive
        if (map.Count == 0) { Console.Error.WriteLine("map 为空"); return 2; }
        string target = UAssets(inputDir).FirstOrDefault(p => Path.GetFileName(p) == fileName);
        if (target == null) { Console.Error.WriteLine($"找不到 {fileName}"); return 2; }
        var asset = Load(target);

        FPackageIndex ImpIdx(string obj) { for (int i = 0; i < asset.Imports.Count; i++) if (Nm(asset.Imports[i].ObjectName) == obj) return FPackageIndex.FromImport(i); return null; }
        var enginePkg = ImpIdx("/Script/Engine") ?? asset.AddImport(new Import("/Script/CoreUObject", "Package", new FPackageIndex(0), "/Script/Engine", false, asset));
        FPackageIndex EnsureClass(string cls) => ImpIdx(cls) ?? asset.AddImport(new Import("/Script/CoreUObject", "Class", enginePkg, cls, false, asset));
        FPackageIndex EnsureFunc(string fn, FPackageIndex cls) => ImpIdx(fn) ?? asset.AddImport(new Import("/Script/CoreUObject", "Object", cls, fn, false, asset));
        var convIdx = EnsureFunc("Conv_TextToString", EnsureClass("KismetTextLibrary"));
        // case-INSENSITIVE: saved data can differ in case from the live option names (UE's JSON
        // serializer lowercases the first letter of map keys, e.g. "lower_Eyelid_Right", and the
        // animation data itself has variants like Lose_idle/Lose_Idle); FNames here keep their casing.
        var eqIdx = EnsureFunc("EqualEqual_StriStri", EnsureClass("KismetStringLibrary"));

        // find the statement `<tbFilter TextBlock>.SetText(<var read>)`
        StructExport se = null; int si = -1; EX_Context origCtx = null; EX_VirtualFunction origSet = null;
        foreach (var exp in asset.Exports)
        {
            if (exp is not StructExport s || s.ScriptBytecode == null) continue;
            for (int i = 0; i < s.ScriptBytecode.Length; i++)
                if (s.ScriptBytecode[i] is EX_Context c && c.ObjectExpression is EX_InstanceVariable tb && PtrName(tb.Variable).Contains(tbFilter)
                    && c.ContextExpression is EX_VirtualFunction vf && Nm(vf.VirtualFunctionName) == "SetText"
                    && vf.Parameters != null && vf.Parameters.Length >= 1 && CloneVarRead(vf.Parameters[0]) != null
                    && (argFilter == null || (vf.Parameters[0] is EX_VariableBase av && PtrName(av.Variable) == argFilter)))
                { se = s; si = i; origCtx = c; origSet = vf; break; }
            if (se != null) break;
        }
        if (se == null) { Console.Error.WriteLine($"找不到 {tbFilter}.SetText({argFilter ?? "<变量或其成员>"}) 语句"); return 3; }
        var tbPtr = ((EX_InstanceVariable)origCtx.ObjectExpression).Variable;
        var origArg = origSet.Parameters[0];
        var setFName = origSet.VirtualFunctionName;

        var (allPre, total) = VisitAll(asset, se.ScriptBytecode);
        var offPre = new Dictionary<KismetExpression, uint>(); foreach (var (e, o) in allPre) offPre[e] = o;
        uint P = offPre[origCtx];                          // insertion offset (before original SetText)
        var origNext = se.ScriptBytecode[si + 1];          // statement after original SetText

        var newStmts = new List<KismetExpression>();
        var jins = new List<EX_JumpIfNot>();
        var jmps = new List<EX_Jump>();
        foreach (var kv in map)
        {
            var conv = new EX_CallMath { StackNode = convIdx, Parameters = new KismetExpression[] { CloneVarRead(origArg) } };
            var cmp = new EX_CallMath { StackNode = eqIdx, Parameters = new KismetExpression[] { conv, new EX_StringConst { Value = kv.Key } } };
            var jin = new EX_JumpIfNot { CodeOffset = 0, BooleanExpression = cmp };
            var setZh = new EX_VirtualFunction { VirtualFunctionName = setFName, Parameters = new KismetExpression[] { new EX_TextConst { Value = new FScriptText { TextLiteralType = EBlueprintTextLiteralType.InvariantText, InvariantLiteralString = new EX_UnicodeStringConst { Value = kv.Value } } } } };
            var ctxZh = new EX_Context { ObjectExpression = new EX_InstanceVariable { Variable = tbPtr }, PropertyType = origCtx.PropertyType, RValuePointer = origCtx.RValuePointer, ContextExpression = setZh, Offset = setZh.GetSize(asset) };
            var jmp = new EX_Jump { CodeOffset = 0 };
            newStmts.Add(jin); newStmts.Add(ctxZh); newStmts.Add(jmp);
            jins.Add(jin); jmps.Add(jmp);
        }
        var listStmts = se.ScriptBytecode.ToList();
        listStmts.InsertRange(si, newStmts);            // insert BEFORE original SetText
        se.ScriptBytecode = listStmts.ToArray();

        int D = newStmts.Sum(s => (int)s.GetSize(asset));
        var deltas = new List<(uint pos, int delta)> { (P, D) };   // shift offsets strictly > P; jumps to P land on new code
        foreach (var (e, o) in allPre) foreach (var (get, set) in AbsOffsets(e)) set(ShiftBy(get(), deltas));
        var fnDeltas = new Dictionary<string, List<(uint pos, int delta)>> { { Nm(se.ObjectName) ?? "", deltas } };
        foreach (var exp in asset.Exports)
        {
            if (exp is not StructExport s2 || s2.ScriptBytecode == null) continue;
            var (all2, _) = VisitAll(asset, s2.ScriptBytecode);
            foreach (var (e, o) in all2) if (e is EX_FinalFunction ff && ff.Parameters != null && ff.Parameters.Length > 0 && ff.Parameters[0] is EX_IntConst ic)
            { string tgt = ResolveIdx(asset, ff.StackNode); if (tgt != null && fnDeltas.TryGetValue(tgt, out var dl)) ic.Value = (int)ShiftBy((uint)ic.Value, dl); }
        }

        var (allPost, _) = VisitAll(asset, se.ScriptBytecode);
        var offPost = new Dictionary<KismetExpression, uint>(); foreach (var (e, o) in allPost) offPost[e] = o;
        uint afterOrig = offPost[origNext];
        for (int i = 0; i < jins.Count; i++)
        {
            jins[i].CodeOffset = (i + 1 < jins.Count) ? offPost[(KismetExpression)jins[i + 1]] : offPost[origCtx]; // no match -> next check, or original SetText
            jmps[i].CodeOffset = afterOrig;                                                                        // match -> skip original SetText
        }

        int bad = VerifyBytecode(asset);
        Console.WriteLine($"insert D={D} P={P} entries={map.Count} verify-fail={bad}");
        if (bad > 0) { Console.Error.WriteLine("校验不过"); return 4; }
        asset.Write(target);
        try { var rt = new UAsset(target, EV, Maps); if (VerifyBytecode(rt) > 0) throw new Exception("post-write inconsistent"); }
        catch (Exception ve) { Console.Error.WriteLine("重解析失败: " + ve.Message); return 5; }
        Console.WriteLine($"[ok] 渲染层查表注入完成（In Text 不动，{map.Count} 条）");
        return 0;
    }

    // Translate a name embedded in `Concat(nameVar, "<suffix>")` (e.g. the "{name} changes" title):
    // reassign the display-only name String local to ZH just before the concat. Safe iff that local
    // feeds only the display concat (verified for WBP_Editor's Text_DetailsTitle).
    static int InjectConcat(string inputDir, string mapJson, string fileName, string suffix)
    {
        var raw = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(mapJson)) ?? new Dictionary<string, string>();
        var map = raw.Where(kv => !string.IsNullOrEmpty(kv.Value) && kv.Value != kv.Key)
                     .GroupBy(kv => kv.Key.ToLowerInvariant()).Select(g => g.First()).ToList();   // compare is case-insensitive
        if (map.Count == 0) { Console.Error.WriteLine("map 为空"); return 2; }
        string target = UAssets(inputDir).FirstOrDefault(p => Path.GetFileName(p) == fileName);
        if (target == null) { Console.Error.WriteLine($"找不到 {fileName}"); return 2; }
        var asset = Load(target);

        FPackageIndex ImpIdx(string obj) { for (int i = 0; i < asset.Imports.Count; i++) if (Nm(asset.Imports[i].ObjectName) == obj) return FPackageIndex.FromImport(i); return null; }
        var enginePkg = ImpIdx("/Script/Engine") ?? asset.AddImport(new Import("/Script/CoreUObject", "Package", new FPackageIndex(0), "/Script/Engine", false, asset));
        FPackageIndex EnsureClass(string cls) => ImpIdx(cls) ?? asset.AddImport(new Import("/Script/CoreUObject", "Class", enginePkg, cls, false, asset));
        FPackageIndex EnsureFunc(string fn, FPackageIndex cls) => ImpIdx(fn) ?? asset.AddImport(new Import("/Script/CoreUObject", "Object", cls, fn, false, asset));
        // case-INSENSITIVE: saved data can differ in case from the live option names (UE's JSON
        // serializer lowercases the first letter of map keys, e.g. "lower_Eyelid_Right", and the
        // animation data itself has variants like Lose_idle/Lose_Idle); FNames here keep their casing.
        var eqIdx = EnsureFunc("EqualEqual_StriStri", EnsureClass("KismetStringLibrary"));

        // find `Let X = Concat_StrStr(nameVar, "<suffix>")`
        StructExport se = null; int ci = -1; KismetExpression nameArg = null;
        foreach (var exp in asset.Exports)
        {
            if (exp is not StructExport s || s.ScriptBytecode == null) continue;
            for (int i = 0; i < s.ScriptBytecode.Length; i++)
                if (s.ScriptBytecode[i] is EX_Let lt && lt.Expression is EX_CallMath cm && ResolveIdx(asset, cm.StackNode) == "Concat_StrStr"
                    && cm.Parameters != null && cm.Parameters.Length >= 2 && ExprStr(cm.Parameters[1]) == suffix
                    && cm.Parameters[0] is EX_VariableBase)
                { se = s; ci = i; nameArg = cm.Parameters[0]; break; }
            if (se != null) break;
        }
        if (se == null) { Console.Error.WriteLine($"找不到 Concat(name, \"{suffix}\") 语句"); return 3; }
        var namePtr = ((EX_VariableBase)nameArg).Variable;

        var (allPre, total) = VisitAll(asset, se.ScriptBytecode);
        var offPre = new Dictionary<KismetExpression, uint>(); foreach (var (e, o) in allPre) offPre[e] = o;
        var origConcat = se.ScriptBytecode[ci];
        uint P = offPre[origConcat];

        var newStmts = new List<KismetExpression>();
        var jins = new List<EX_JumpIfNot>();
        foreach (var kv in map)
        {
            var cmp = new EX_CallMath { StackNode = eqIdx, Parameters = new KismetExpression[] { CloneVarRead(nameArg), new EX_StringConst { Value = kv.Key } } };
            var jin = new EX_JumpIfNot { CodeOffset = 0, BooleanExpression = cmp };
            var let = new EX_Let { Value = namePtr, Variable = CloneVarRead(nameArg), Expression = new EX_UnicodeStringConst { Value = kv.Value } };
            newStmts.Add(jin); newStmts.Add(let); jins.Add(jin);
        }
        var listStmts = se.ScriptBytecode.ToList();
        listStmts.InsertRange(ci, newStmts);
        se.ScriptBytecode = listStmts.ToArray();

        int D = newStmts.Sum(s => (int)s.GetSize(asset));
        var deltas = new List<(uint pos, int delta)> { (P == 0 ? 0u : P - 1, D) };
        foreach (var (e, o) in allPre) foreach (var (get, set) in AbsOffsets(e)) set(ShiftBy(get(), deltas));
        var fnDeltas = new Dictionary<string, List<(uint pos, int delta)>> { { Nm(se.ObjectName) ?? "", deltas } };
        foreach (var exp in asset.Exports)
        {
            if (exp is not StructExport s2 || s2.ScriptBytecode == null) continue;
            var (all2, _) = VisitAll(asset, s2.ScriptBytecode);
            foreach (var (e, o) in all2) if (e is EX_FinalFunction ff && ff.Parameters != null && ff.Parameters.Length > 0 && ff.Parameters[0] is EX_IntConst ic)
            { string tgt = ResolveIdx(asset, ff.StackNode); if (tgt != null && fnDeltas.TryGetValue(tgt, out var dl)) ic.Value = (int)ShiftBy((uint)ic.Value, dl); }
        }
        var (allPost, _) = VisitAll(asset, se.ScriptBytecode);
        var offPost = new Dictionary<KismetExpression, uint>(); foreach (var (e, o) in allPost) offPost[e] = o;
        for (int i = 0; i < jins.Count; i++)
            jins[i].CodeOffset = (i + 1 < jins.Count) ? offPost[(KismetExpression)jins[i + 1]] : offPost[origConcat];

        int bad = VerifyBytecode(asset);
        Console.WriteLine($"insert D={D} P={P} entries={map.Count} verify-fail={bad}");
        if (bad > 0) { Console.Error.WriteLine("校验不过"); return 4; }
        asset.Write(target);
        try { var rt = new UAsset(target, EV, Maps); if (VerifyBytecode(rt) > 0) throw new Exception("post-write inconsistent"); }
        catch (Exception ve) { Console.Error.WriteLine("重解析失败: " + ve.Message); return 5; }
        Console.WriteLine($"[ok] 标题名字查表注入完成（{map.Count} 条）");
        return 0;
    }

    // Source-side display translation for ComboBoxKey generator functions: before every
    // `SetTextPropertyByName(w, "In Text", <var>)`, reassign the FText local <var> to Text("ZH") when
    // Conv_TextToString(<var>) matches (case-insensitive). The combo's value is the Item FName, so the
    // content widget's In Text is display-only here. Handles every matching function in the file.
    static int InjectSetProp(string inputDir, string mapJson, string fileName, string fnFilter = null)
    {
        var raw = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(mapJson)) ?? new Dictionary<string, string>();
        var map = raw.Where(kv => !string.IsNullOrEmpty(kv.Value) && kv.Value != kv.Key)
                     .GroupBy(kv => kv.Key.ToLowerInvariant()).Select(g => g.First()).ToList();
        if (map.Count == 0) { Console.Error.WriteLine("map 为空"); return 2; }
        string target = UAssets(inputDir).FirstOrDefault(p => Path.GetFileName(p) == fileName);
        if (target == null) { Console.Error.WriteLine($"找不到 {fileName}"); return 2; }
        var asset = Load(target);
        FPackageIndex ImpIdx(string obj) { for (int i = 0; i < asset.Imports.Count; i++) if (Nm(asset.Imports[i].ObjectName) == obj) return FPackageIndex.FromImport(i); return null; }
        var enginePkg = ImpIdx("/Script/Engine") ?? asset.AddImport(new Import("/Script/CoreUObject", "Package", new FPackageIndex(0), "/Script/Engine", false, asset));
        FPackageIndex EnsureClass(string cls) => ImpIdx(cls) ?? asset.AddImport(new Import("/Script/CoreUObject", "Class", enginePkg, cls, false, asset));
        FPackageIndex EnsureFunc(string fn, FPackageIndex cls) => ImpIdx(fn) ?? asset.AddImport(new Import("/Script/CoreUObject", "Object", cls, fn, false, asset));
        var convIdx = EnsureFunc("Conv_TextToString", EnsureClass("KismetTextLibrary"));
        var eqIdx = EnsureFunc("EqualEqual_StriStri", EnsureClass("KismetStringLibrary"));

        var fnDeltas = new Dictionary<string, List<(uint pos, int delta)>>();
        int funcs = 0;
        foreach (var exp in asset.Exports)
        {
            if (exp is not StructExport se || se.ScriptBytecode == null) continue;
            if (fnFilter != null && !(Nm(exp.ObjectName) ?? "").Contains(fnFilter)) continue;
            int si = -1; KismetExpression varArg = null;
            for (int i = 0; i < se.ScriptBytecode.Length; i++)
                if (se.ScriptBytecode[i] is EX_CallMath cm && ResolveIdx(asset, cm.StackNode) == "SetTextPropertyByName"
                    && cm.Parameters != null && cm.Parameters.Length >= 3
                    && cm.Parameters[1] is EX_NameConst nc && Nm(nc.Value) == "In Text" && cm.Parameters[2] is EX_VariableBase)
                { si = i; varArg = cm.Parameters[2]; break; }
            if (si < 0) continue;

            var varPtr = ((EX_VariableBase)varArg).Variable;
            var (allPre, _) = VisitAll(asset, se.ScriptBytecode);
            var offPre = new Dictionary<KismetExpression, uint>(); foreach (var (e, o) in allPre) offPre[e] = o;
            var origStmt = se.ScriptBytecode[si];
            uint P = offPre[origStmt];

            var newStmts = new List<KismetExpression>();
            var jins = new List<EX_JumpIfNot>();
            foreach (var kv in map)
            {
                var conv = new EX_CallMath { StackNode = convIdx, Parameters = new KismetExpression[] { CloneVarRead(varArg) } };
                var cmp = new EX_CallMath { StackNode = eqIdx, Parameters = new KismetExpression[] { conv, new EX_StringConst { Value = kv.Key } } };
                var jin = new EX_JumpIfNot { CodeOffset = 0, BooleanExpression = cmp };
                var let = new EX_Let { Value = varPtr, Variable = CloneVarRead(varArg), Expression = new EX_TextConst { Value = new FScriptText { TextLiteralType = EBlueprintTextLiteralType.InvariantText, InvariantLiteralString = new EX_UnicodeStringConst { Value = kv.Value } } } };
                newStmts.Add(jin); newStmts.Add(let); jins.Add(jin);
            }
            var list = se.ScriptBytecode.ToList(); list.InsertRange(si, newStmts); se.ScriptBytecode = list.ToArray();

            int D = newStmts.Sum(s => (int)s.GetSize(asset));
            var deltas = new List<(uint pos, int delta)> { (P, D) };   // jumps to P land on the new code
            foreach (var (e, o) in allPre) foreach (var (get, set) in AbsOffsets(e)) set(ShiftBy(get(), deltas));
            foreach (var (e, o) in allPre) switch (e) { case EX_Context c: c.Offset = c.ContextExpression.GetSize(asset); break; case EX_Skip s: s.CodeOffset = s.SkipExpression.GetSize(asset); break; }
            var (allPost, _) = VisitAll(asset, se.ScriptBytecode);
            var offPost = new Dictionary<KismetExpression, uint>(); foreach (var (e, o) in allPost) offPost[e] = o;
            for (int i = 0; i < jins.Count; i++)
                jins[i].CodeOffset = (i + 1 < jins.Count) ? offPost[(KismetExpression)jins[i + 1]] : offPost[origStmt];
            fnDeltas[Nm(exp.ObjectName)] = deltas;
            funcs++;
            Console.WriteLine($"  {Nm(exp.ObjectName)}: insert D={D} @P={P}");
        }
        if (funcs == 0) { Console.Error.WriteLine($"{fileName} 里没有 SetTextPropertyByName(\"In Text\", <var>)"); return 3; }
        foreach (var exp in asset.Exports)
        {
            if (exp is not StructExport s2 || s2.ScriptBytecode == null) continue;
            var (all2, _) = VisitAll(asset, s2.ScriptBytecode);
            foreach (var (e, o) in all2) if (e is EX_FinalFunction ff && ff.Parameters != null && ff.Parameters.Length > 0 && ff.Parameters[0] is EX_IntConst ic)
            { string tgt = ResolveIdx(asset, ff.StackNode); if (tgt != null && fnDeltas.TryGetValue(tgt, out var dl)) ic.Value = (int)ShiftBy((uint)ic.Value, dl); }
        }
        int bad = VerifyBytecode(asset);
        Console.WriteLine($"funcs={funcs} entries={map.Count} verify-fail={bad}");
        if (bad > 0) return 4;
        asset.Write(target);
        try { var rt = new UAsset(target, EV, Maps); if (VerifyBytecode(rt) > 0) throw new Exception("post-write inconsistent"); }
        catch (Exception ve) { Console.Error.WriteLine("重解析失败: " + ve.Message); return 5; }
        Console.WriteLine($"[ok] 源头显示翻译完成：{fileName}（{funcs} 个生成函数）");
        return 0;
    }

    // Bidirectional mapping for plain ComboBoxString pickers (display == value). In every function
    // whose name contains <fnFilter>, find the first top-level statement that calls <anchorFunc> with
    // the String variable <varName> as an argument, and insert before it a reassignment chain:
    //   fwd: var = ZH  when var ~= EN      (e.g. before AddOption, so the list shows Chinese)
    //   rev: var = EN  when var ~= ZH      (e.g. before the selection is used as the real key)
    // Reverse mapping needs distinct Chinese values, so ambiguous maps are rejected.
    static int InjectVarMap(string inputDir, string mapJson, string fileName, string fnFilter, string anchorFunc, string varName, string dir)
    {
        bool rev = dir == "rev";
        var raw = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(mapJson)) ?? new Dictionary<string, string>();
        var map = raw.Where(kv => !string.IsNullOrEmpty(kv.Value) && kv.Value != kv.Key)
                     .GroupBy(kv => kv.Key.ToLowerInvariant()).Select(g => g.First()).ToList();
        if (map.Count == 0) { Console.Error.WriteLine("map 为空"); return 2; }
        if (rev)
        {
            var dup = map.GroupBy(kv => kv.Value).Where(g => g.Count() > 1).ToList();
            if (dup.Count > 0)
            {
                Console.Error.WriteLine("反向映射需要中文译名唯一，以下译名对应了多个英文：");
                foreach (var g in dup) Console.Error.WriteLine($"  {g.Key} <- {string.Join(", ", g.Select(x => x.Key))}");
                return 6;
            }
        }
        string target = UAssets(inputDir).FirstOrDefault(p => Path.GetFileName(p) == fileName);
        if (target == null) { Console.Error.WriteLine($"找不到 {fileName}"); return 2; }
        var asset = Load(target);
        FPackageIndex ImpIdx(string obj) { for (int i = 0; i < asset.Imports.Count; i++) if (Nm(asset.Imports[i].ObjectName) == obj) return FPackageIndex.FromImport(i); return null; }
        var enginePkg = ImpIdx("/Script/Engine") ?? asset.AddImport(new Import("/Script/CoreUObject", "Package", new FPackageIndex(0), "/Script/Engine", false, asset));
        FPackageIndex EnsureClass(string cls) => ImpIdx(cls) ?? asset.AddImport(new Import("/Script/CoreUObject", "Class", enginePkg, cls, false, asset));
        FPackageIndex EnsureFunc(string fn, FPackageIndex cls) => ImpIdx(fn) ?? asset.AddImport(new Import("/Script/CoreUObject", "Object", cls, fn, false, asset));
        var eqIdx = EnsureFunc("EqualEqual_StriStri", EnsureClass("KismetStringLibrary"));
        KismetExpression Str(string s) => s.All(c => c < 128) ? new EX_StringConst { Value = s } : new EX_UnicodeStringConst { Value = s };

        // does this call expression call <anchorFunc> with a read of <varName> as a parameter?
        string CallName(KismetExpression e) => e switch
        {
            EX_VirtualFunction vf => Nm(vf.VirtualFunctionName),
            EX_FinalFunction ff => ResolveIdx(asset, ff.StackNode),
            _ => null
        };
        KismetExpression FindVarArg(KismetExpression stmt)
        {
            KismetExpression hit = null; uint off = 0;
            stmt.Visit(asset, ref off, (e, o) =>
            {
                if (hit != null || CallName(e) != anchorFunc) return;
                var ps = e is EX_VirtualFunction v ? v.Parameters : (e as EX_FinalFunction)?.Parameters;
                if (ps == null) return;
                foreach (var p in ps) if (p is EX_VariableBase vb && PtrName(vb.Variable).Contains(varName)) { hit = p; break; }
            });
            return hit;
        }

        var fnDeltas = new Dictionary<string, List<(uint pos, int delta)>>();
        int funcs = 0;
        foreach (var exp in asset.Exports)
        {
            if (exp is not StructExport se || se.ScriptBytecode == null) continue;
            if (!(Nm(exp.ObjectName) ?? "").Contains(fnFilter)) continue;
            int si = -1; KismetExpression varArg = null;
            for (int i = 0; i < se.ScriptBytecode.Length && si < 0; i++)
            {
                var a = FindVarArg(se.ScriptBytecode[i]);
                if (a != null) { si = i; varArg = a; }
            }
            if (si < 0) continue;
            var varPtr = ((EX_VariableBase)varArg).Variable;
            var (allPre, _) = VisitAll(asset, se.ScriptBytecode);
            var offPre = new Dictionary<KismetExpression, uint>(); foreach (var (e, o) in allPre) offPre[e] = o;
            var origStmt = se.ScriptBytecode[si];
            uint P = offPre[origStmt];

            var newStmts = new List<KismetExpression>();
            var jins = new List<EX_JumpIfNot>();
            foreach (var kv in map)
            {
                string from = rev ? kv.Value : kv.Key, to = rev ? kv.Key : kv.Value;
                var cmp = new EX_CallMath { StackNode = eqIdx, Parameters = new KismetExpression[] { CloneVarRead(varArg), Str(from) } };
                var jin = new EX_JumpIfNot { CodeOffset = 0, BooleanExpression = cmp };
                var let = new EX_Let { Value = varPtr, Variable = CloneVarRead(varArg), Expression = Str(to) };
                newStmts.Add(jin); newStmts.Add(let); jins.Add(jin);
            }
            var list = se.ScriptBytecode.ToList(); list.InsertRange(si, newStmts); se.ScriptBytecode = list.ToArray();
            int D = newStmts.Sum(s => (int)s.GetSize(asset));
            var deltas = new List<(uint pos, int delta)> { (P, D) };   // jumps/entries to P land on the new code
            foreach (var (e, o) in allPre) foreach (var (get, set) in AbsOffsets(e)) set(ShiftBy(get(), deltas));
            foreach (var (e, o) in allPre) switch (e) { case EX_Context c: c.Offset = c.ContextExpression.GetSize(asset); break; case EX_Skip s: s.CodeOffset = s.SkipExpression.GetSize(asset); break; }
            var (allPost, _) = VisitAll(asset, se.ScriptBytecode);
            var offPost = new Dictionary<KismetExpression, uint>(); foreach (var (e, o) in allPost) offPost[e] = o;
            for (int i = 0; i < jins.Count; i++)
                jins[i].CodeOffset = (i + 1 < jins.Count) ? offPost[(KismetExpression)jins[i + 1]] : offPost[origStmt];
            fnDeltas[Nm(exp.ObjectName)] = deltas;
            funcs++;
            Console.WriteLine($"  {Nm(exp.ObjectName)}: before {anchorFunc}({varName}) insert D={D} @P={P} [{dir}]");
        }
        if (funcs == 0) { Console.Error.WriteLine($"{fileName} 里找不到 {anchorFunc}(…{varName}…)（函数过滤：{fnFilter}）"); return 3; }
        foreach (var exp in asset.Exports)
        {
            if (exp is not StructExport s2 || s2.ScriptBytecode == null) continue;
            var (all2, _) = VisitAll(asset, s2.ScriptBytecode);
            foreach (var (e, o) in all2) if (e is EX_FinalFunction ff && ff.Parameters != null && ff.Parameters.Length > 0 && ff.Parameters[0] is EX_IntConst ic)
            { string tgt = ResolveIdx(asset, ff.StackNode); if (tgt != null && fnDeltas.TryGetValue(tgt, out var dl)) ic.Value = (int)ShiftBy((uint)ic.Value, dl); }
        }
        int bad = VerifyBytecode(asset);
        Console.WriteLine($"funcs={funcs} entries={map.Count} verify-fail={bad}");
        if (bad > 0) return 4;
        asset.Write(target);
        try { var rt = new UAsset(target, EV, Maps); if (VerifyBytecode(rt) > 0) throw new Exception("post-write inconsistent"); }
        catch (Exception ve) { Console.Error.WriteLine("重解析失败: " + ve.Message); return 5; }
        Console.WriteLine($"[ok] 变量映射注入完成：{fileName} / {varName} [{dir}]");
        return 0;
    }

    // Diagnostic: in <file>, replace the argument of the fallback `<tbFilter>.SetText(<var>)` with
    // Conv_StringToText("[" + Conv_TextToString(<var>) + "]") so the UI shows exactly what value reached it.
    static int InjectDiag(string inputDir, string fileName, string tbFilter)
    {
        string target = UAssets(inputDir).FirstOrDefault(p => Path.GetFileName(p) == fileName);
        if (target == null) { Console.Error.WriteLine($"找不到 {fileName}"); return 2; }
        var asset = Load(target);
        FPackageIndex ImpIdx(string obj) { for (int i = 0; i < asset.Imports.Count; i++) if (Nm(asset.Imports[i].ObjectName) == obj) return FPackageIndex.FromImport(i); return null; }
        var enginePkg = ImpIdx("/Script/Engine") ?? asset.AddImport(new Import("/Script/CoreUObject", "Package", new FPackageIndex(0), "/Script/Engine", false, asset));
        FPackageIndex EnsureClass(string cls) => ImpIdx(cls) ?? asset.AddImport(new Import("/Script/CoreUObject", "Class", enginePkg, cls, false, asset));
        FPackageIndex EnsureFunc(string fn, FPackageIndex cls) => ImpIdx(fn) ?? asset.AddImport(new Import("/Script/CoreUObject", "Object", cls, fn, false, asset));
        var t2s = EnsureFunc("Conv_TextToString", EnsureClass("KismetTextLibrary"));
        var s2t = EnsureFunc("Conv_StringToText", EnsureClass("KismetTextLibrary"));
        var cat = EnsureFunc("Concat_StrStr", EnsureClass("KismetStringLibrary"));

        var fnDeltas = new Dictionary<string, List<(uint pos, int delta)>>();
        int edits = 0;
        foreach (var exp in asset.Exports)
        {
            if (exp is not StructExport se || se.ScriptBytecode == null) continue;
            var (all, total) = VisitAll(asset, se.ScriptBytecode);
            var preOff = new Dictionary<KismetExpression, uint>(); foreach (var (e, o) in all) preOff[e] = o;
            var deltas = new List<(uint pos, int delta)>();
            foreach (var (e, o) in all)
            {
                if (e is EX_Context c && c.ObjectExpression is EX_InstanceVariable tb && PtrName(tb.Variable).Contains(tbFilter)
                    && c.ContextExpression is EX_VirtualFunction vf && Nm(vf.VirtualFunctionName) == "SetText"
                    && vf.Parameters != null && vf.Parameters.Length >= 1 && vf.Parameters[0] is EX_VariableBase)
                {
                    var old = vf.Parameters[0];
                    if (!preOff.TryGetValue(old, out uint pos)) continue;
                    var inner = new EX_CallMath { StackNode = t2s, Parameters = new KismetExpression[] { CloneVarRead(old) } };
                    var c1 = new EX_CallMath { StackNode = cat, Parameters = new KismetExpression[] { new EX_StringConst { Value = "[" }, inner } };
                    var c2 = new EX_CallMath { StackNode = cat, Parameters = new KismetExpression[] { c1, new EX_StringConst { Value = "]" } } };
                    var neu = new EX_CallMath { StackNode = s2t, Parameters = new KismetExpression[] { c2 } };
                    vf.Parameters[0] = neu;
                    deltas.Add((pos, (int)neu.GetSize(asset) - (int)old.GetSize(asset)));
                    edits++;
                }
            }
            if (deltas.Count == 0) continue;
            foreach (var (e, o) in all) foreach (var (get, set) in AbsOffsets(e)) set(ShiftBy(get(), deltas));
            foreach (var (e, o) in all) switch (e) { case EX_Context cc: cc.Offset = cc.ContextExpression.GetSize(asset); break; case EX_Skip s: s.CodeOffset = s.SkipExpression.GetSize(asset); break; }
            fnDeltas[Nm(exp.ObjectName)] = deltas;
        }
        if (edits == 0) { Console.Error.WriteLine("没找到回退 SetText"); return 3; }
        foreach (var exp in asset.Exports)
        {
            if (exp is not StructExport s2 || s2.ScriptBytecode == null) continue;
            var (all2, _) = VisitAll(asset, s2.ScriptBytecode);
            foreach (var (e, o) in all2) if (e is EX_FinalFunction ff && ff.Parameters != null && ff.Parameters.Length > 0 && ff.Parameters[0] is EX_IntConst ic)
            { string tgt = ResolveIdx(asset, ff.StackNode); if (tgt != null && fnDeltas.TryGetValue(tgt, out var dl)) ic.Value = (int)ShiftBy((uint)ic.Value, dl); }
        }
        int bad = VerifyBytecode(asset);
        Console.WriteLine($"diag edits={edits} verify-fail={bad}");
        if (bad > 0) return 4;
        asset.Write(target);
        try { var rt = new UAsset(target, EV, Maps); if (VerifyBytecode(rt) > 0) throw new Exception("post-write inconsistent"); }
        catch (Exception ve) { Console.Error.WriteLine("重解析失败: " + ve.Message); return 5; }
        Console.WriteLine($"[ok] 诊断注入完成：{fileName} 的回退显示改为 [值]");
        return 0;
    }

    // Diagnostic: add ONLY the Conv_TextToString import chain (unused), no bytecode change.
    static int InjectImportOnly(string inputDir)
    {
        string target = UAssets(inputDir).FirstOrDefault(p => Path.GetFileName(p) == "WBP_DropdownButtonContent.uasset");
        if (target == null) { Console.Error.WriteLine("找不到 widget"); return 2; }
        var asset = Load(target);
        FPackageIndex ImpIdx(string obj) { for (int i = 0; i < asset.Imports.Count; i++) if (Nm(asset.Imports[i].ObjectName) == obj) return FPackageIndex.FromImport(i); return null; }
        var enginePkg = ImpIdx("/Script/Engine") ?? asset.AddImport(new Import("/Script/CoreUObject", "Package", new FPackageIndex(0), "/Script/Engine", false, asset));
        var ktlClass = ImpIdx("KismetTextLibrary") ?? asset.AddImport(new Import("/Script/CoreUObject", "Class", enginePkg, "KismetTextLibrary", false, asset));
        var convFunc = ImpIdx("Conv_TextToString") ?? asset.AddImport(new Import("/Script/CoreUObject", "Object", ktlClass, "Conv_TextToString", false, asset));
        Console.WriteLine($"engine={enginePkg.Index} ktl={ktlClass.Index} conv={convFunc.Index}");
        asset.Write(target);
        Console.WriteLine("[ok] 仅加 import（未使用），字节码未动");
        return 0;
    }

    // Diagnostic: inject a SwitchValue with a TYPED index (In Text, FText) and an always-match case,
    // NO import / NO CallMath. Isolates "SwitchValue execution + offsets" from "Conv_TextToString".
    static int InjectSwTest(string inputDir, string testText)
    {
        string target = UAssets(inputDir).FirstOrDefault(p => Path.GetFileName(p) == "WBP_DropdownButtonContent.uasset");
        if (target == null) { Console.Error.WriteLine("找不到 widget"); return 2; }
        var asset = Load(target);
        var fnDeltas = new Dictionary<string, List<(uint pos, int delta)>>();
        var mySwitches = new List<EX_SwitchValue>();
        int edits = 0;

        foreach (var exp in asset.Exports)
        {
            if (exp is not StructExport se || se.ScriptBytecode == null) continue;
            var (all, total) = VisitAll(asset, se.ScriptBytecode);
            var preOff = new Dictionary<KismetExpression, uint>();
            foreach (var (e, o) in all) preOff[e] = o;
            var deltas = new List<(uint pos, int delta)>();
            foreach (var (e, o) in all)
            {
                if (e is EX_VirtualFunction vf && Nm(vf.VirtualFunctionName) == "SetText"
                    && vf.Parameters != null && vf.Parameters.Length >= 1
                    && vf.Parameters[0] is EX_InstanceVariable iv && PtrName(iv.Variable).Contains("In Text"))
                {
                    var old = vf.Parameters[0];
                    if (!preOff.TryGetValue(old, out uint pos)) continue;
                    var ptr = ((EX_InstanceVariable)old).Variable;
                    EX_InstanceVariable MkIn() => new EX_InstanceVariable { Variable = ptr };
                    var termZh = new EX_TextConst { Value = new FScriptText { TextLiteralType = EBlueprintTextLiteralType.InvariantText, InvariantLiteralString = new EX_UnicodeStringConst { Value = testText } } };
                    var sw = new EX_SwitchValue { EndGotoOffset = 0, IndexTerm = MkIn(), Cases = new[] { new FKismetSwitchCase(MkIn(), 0u, termZh) }, DefaultTerm = MkIn() };
                    vf.Parameters[0] = sw;
                    mySwitches.Add(sw);
                    deltas.Add((pos, (int)sw.GetSize(asset) - (int)old.GetSize(asset)));
                    edits++;
                }
            }
            if (deltas.Count == 0) continue;
            foreach (var (e, o) in all) foreach (var (get, set) in AbsOffsets(e)) set(ShiftBy(get(), deltas));
            foreach (var (e, o) in all) switch (e) { case EX_Context c: c.Offset = c.ContextExpression.GetSize(asset); break; case EX_Skip s: s.CodeOffset = s.SkipExpression.GetSize(asset); break; }
            fnDeltas[Nm(exp.ObjectName)] = deltas;
        }
        if (edits == 0) { Console.Error.WriteLine("no inject point"); return 3; }
        foreach (var exp in asset.Exports)
        {
            if (exp is not StructExport se || se.ScriptBytecode == null) continue;
            var (all, _) = VisitAll(asset, se.ScriptBytecode);
            foreach (var (e, o) in all) if (e is EX_FinalFunction ff && ff.Parameters != null && ff.Parameters.Length > 0 && ff.Parameters[0] is EX_IntConst ic) { string tgt = ResolveIdx(asset, ff.StackNode); if (tgt != null && fnDeltas.TryGetValue(tgt, out var dl)) ic.Value = (int)ShiftBy((uint)ic.Value, dl); }
        }
        foreach (var exp in asset.Exports)
        {
            if (exp is not StructExport se || se.ScriptBytecode == null) continue;
            var (all, _) = VisitAll(asset, se.ScriptBytecode);
            var offOf = new Dictionary<KismetExpression, uint>();
            foreach (var (e, o) in all) offOf[e] = o;
            foreach (var (e, o) in all) if (e is EX_SwitchValue sw && mySwitches.Contains(sw))
            {
                sw.EndGotoOffset = o + sw.GetSize(asset);
                for (int i = 0; i < sw.Cases.Length; i++) sw.Cases[i].NextOffset = offOf[(i + 1 < sw.Cases.Length) ? sw.Cases[i + 1].CaseIndexValueTerm : sw.DefaultTerm];
                Console.WriteLine($"  switch @{o}: EndGoto={sw.EndGotoOffset} nexts=[{string.Join(",", sw.Cases.Select(c => c.NextOffset))}]");
            }
        }
        int bad = VerifyBytecode(asset);
        Console.WriteLine($"verify-fail: {bad}");
        if (bad > 0) return 4;
        asset.Write(target);
        try { var rt = new UAsset(target, EV, Maps); if (VerifyBytecode(rt) > 0) throw new Exception("post-write inconsistent"); }
        catch (Exception ve) { Console.Error.WriteLine($"reparse fail: {ve.Message}"); return 5; }
        Console.WriteLine($"[ok] SwitchValue 诊断注入完成（typed index, no import）");
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

    // Print the property tree of every NormalExport in files whose name contains <fileSub>.
    static int Props(string inputDir, string fileSub)
    {
        UAsset cur = null;
        string Leaf(PropertyData p) => p switch
        {
            NamePropertyData n => n.Value?.ToString(),
            StrPropertyData s => "\"" + Fs(s.Value) + "\"",
            TextPropertyData t => "text:" + (Fs(t.CultureInvariantString) ?? Fs(t.Value)),
            SoftObjectPropertyData so => "soft:" + Nm(so.Value.AssetPath.PackageName) + "." + Nm(so.Value.AssetPath.AssetName),
            ObjectPropertyData o => "obj:" + (ResolveIdx(cur, o.Value) ?? o.Value?.Index.ToString()),
            _ => p.ToString()
        };
        void Walk(PropertyData p, string ind)
        {
            switch (p)
            {
                case null: return;
                case StructPropertyData st:
                    Console.WriteLine($"{ind}{Nm(st.Name)} <{Nm(st.StructType)}>");
                    if (st.Value != null) foreach (var c in st.Value) Walk(c, ind + "  ");
                    return;
                case ArrayPropertyData a:
                    Console.WriteLine($"{ind}{Nm(a.Name)} [{a.Value?.Length}]");
                    if (a.Value != null) foreach (var c in a.Value) Walk(c, ind + "  ");
                    return;
                case MapPropertyData m:
                    Console.WriteLine($"{ind}{Nm(m.Name)} {{{m.Value?.Count}}}");
                    if (m.Value != null) foreach (var kv in m.Value)
                    {
                        if (kv.Value is StructPropertyData or ArrayPropertyData or MapPropertyData)
                        { Console.WriteLine($"{ind}  key={Leaf(kv.Key)}"); Walk(kv.Value, ind + "    "); }
                        else Console.WriteLine($"{ind}  key={Leaf(kv.Key)} => {Leaf(kv.Value)}");
                    }
                    return;
                default:
                    Console.WriteLine($"{ind}{Nm(p.Name)} = {Leaf(p)}");
                    return;
            }
        }
        foreach (var path in UAssets(inputDir).OrderBy(x => x))
        {
            if (fileSub.Length > 0 && !Path.GetFileName(path).Contains(fileSub)) continue;
            UAsset asset; try { asset = Load(path); } catch (Exception e) { Console.Error.WriteLine($"[skip] {Path.GetFileName(path)}: {e.Message}"); continue; }
            cur = asset;
            foreach (var exp in asset.Exports)
                if (exp is NormalExport ne && ne.Data != null && exp is not StructExport)
                {
                    Console.WriteLine($"==== {Path.GetFileName(path)} :: {Nm(exp.ObjectName)} ====");
                    foreach (var p in ne.Data) Walk(p, "  ");
                }
                else
                {
                    Console.WriteLine($"==== {Path.GetFileName(path)} :: {Nm(exp.ObjectName)} [{exp.GetType().Name}] ====");
                    if (exp is StructExport sx && sx.LoadedProperties != null)
                        foreach (var fp in sx.LoadedProperties)
                        {
                            string extra = fp switch
                            {
                                FMapProperty mp => $" key={mp.KeyProp?.SerializedType} val={mp.ValueProp?.SerializedType}" + (mp.ValueProp is FStructProperty vs ? $"<{ResolveIdx(asset, vs.Struct)}>" : ""),
                                FStructProperty sp => $" <{ResolveIdx(asset, sp.Struct)}>",
                                FArrayProperty ap => $" inner={ap.Inner?.SerializedType}" + (ap.Inner is FStructProperty ins ? $"<{ResolveIdx(asset, ins.Struct)}>" : ""),
                                _ => ""
                            };
                            Console.WriteLine($"  prop {Nm(fp.Name)} : {fp.SerializedType}{extra}");
                        }
                }
        }
        return 0;
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
    // Scoped PER FILE: a bare string is translated only in files whose relative path contains the key
    // (key "" = any file). This is required for common words like "Remove": the WBP_MenuScenarioEntry
    // ComboBox compares the (translated) selected option against a bare literal, so that literal must
    // be translated to the SAME Chinese to keep the dropdown working -- but "Remove" as a bare logic
    // key in OTHER blueprints must stay English.
    static readonly Dictionary<string, HashSet<string>> BareSafeByFile = new()
    {
        [""] = new HashSet<string> { "Showing scenarios: " },
        ["WBP_MenuScenarioEntry"] = new HashSet<string> { "Set Category", "Remove" },
        ["WBP_Editor"] = new HashSet<string> { " changes" },
    };

    static bool IsBareSafe(string rel, string cur)
    {
        foreach (var kv in BareSafeByFile)
            if ((kv.Key.Length == 0 || rel.Contains(kv.Key)) && kv.Value.Contains(cur)) return true;
        return false;
    }

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
    static int ApplyBytecode(UAsset asset, string rel, Dictionary<string, string> map, HashSet<string> used)
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
                if (bare && !IsBareSafe(rel, cur)) return null;
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
            int bcChanges = ApplyBytecode(asset, rel, map, used);
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

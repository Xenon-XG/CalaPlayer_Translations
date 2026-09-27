# -*- coding: utf-8 -*-
"""CalaPlayer 汉化工具 — 本地前端后端。
纯标准库，无需 pip 安装。双击「打开汉化工具.bat」即会启动它并打开浏览器。

  GET  /                 前端页面 index.html
  GET  /api/data         返回 translations + reference + 各名称表 + 游戏是否运行
  POST /api/save         保存 translations.json 和各名称表（保留原有键顺序）
  POST /api/build        运行 rebuild.ps1 -NoPause（应用翻译→打包→装进游戏），回传日志
  GET  /api/status       仅查询游戏是否在运行
"""
import http.server
import json
import os
import socket
import subprocess
import sys
import threading
import webbrowser

HERE = os.path.dirname(os.path.abspath(__file__))


def _find(*cands):
    """返回第一个存在的候选路径；都不存在则返回第一个（供报错用）。
    这样同一份脚本在扁平套件目录和仓库 tools/ 布局下都能找到数据文件。"""
    for c in cands:
        if os.path.isfile(c):
            return c
    return cands[0]


TRANS = _find(os.path.join(HERE, "translations.json"),
              os.path.join(HERE, "..", "translations.json"))
REF = _find(os.path.join(HERE, "所有可翻译文本.json"),
            os.path.join(HERE, "..", "reference", "所有可翻译文本.json"))
INDEX = os.path.join(HERE, "index.html")

# 名称表：只改游戏里「显示」的名字，存盘/加载用的英文键不变。
# unique=True 的表会被反查（[+] 添加选择器选中中文后要换回英文），中文译名不能重复。
NAME_MAPS = [
    ("chars",      "角色名",   "resname_map.json",    False),
    ("anim",       "动作",     "anim_map.json",       True),
    ("morph",      "表情",     "morph_map.json",      True),
    ("bgm",        "背景音乐", "bgm_map.json",        False),
    ("ambient",    "环境音",   "ambient_map.json",    False),
    ("sound",      "音效",     "sound_map.json",      False),
    ("background", "背景",     "background_map.json", False),
]


def map_path(fname):
    return _find(os.path.join(HERE, fname), os.path.join(HERE, "..", fname))
REBUILD = os.path.join(HERE, "rebuild.ps1")


def load_json(path, default):
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return default


def load_names():
    out = []
    for mid, label, fname, unique in NAME_MAPS:
        path = map_path(fname)
        if os.path.isfile(path):
            out.append({"id": mid, "label": label, "file": fname, "unique": unique,
                        "data": load_json(path, {})})
    return out


def save_names(names):
    """写回各名称表：键集合和顺序以原文件为准（键是游戏里的英文名，不能增删）。"""
    n = 0
    for mid, label, fname, unique in NAME_MAPS:
        if mid not in names:
            continue
        path = map_path(fname)
        old = load_json(path, None)
        if old is None:
            continue
        new = names[mid] or {}
        merged = {k: str(new.get(k, v) if new.get(k) is not None else v).strip() for k, v in old.items()}
        with open(path, "w", encoding="utf-8") as f:
            json.dump(merged, f, ensure_ascii=False, indent=2)
            f.write("\n")
        n += 1
    return n


def name_conflicts():
    """需要反查的表里，同一个中文对应多个英文（打包会失败）。"""
    bad = []
    for mid, label, fname, unique in NAME_MAPS:
        if not unique:
            continue
        seen = {}
        for k, v in load_json(map_path(fname), {}).items():
            v = str(v).strip()
            if v and v != k:
                seen.setdefault(v, []).append(k)
        for v, ks in seen.items():
            if len(ks) > 1:
                bad.append("%s：「%s」同时对应 %s" % (label, v, "、".join(ks)))
    return bad


def game_running():
    """CalaPlayer.exe 是否在运行（打包前必须关游戏）。"""
    try:
        out = subprocess.run(
            ["tasklist", "/FI", "IMAGENAME eq CalaPlayer.exe", "/NH"],
            capture_output=True, text=True, timeout=10,
        ).stdout
        return "CalaPlayer.exe" in out
    except Exception:
        return False


def save_translations(new_map):
    """写回 translations.json：先按原文件里的键顺序，其余新键按字母序追加。"""
    old = load_json(TRANS, {})
    ordered = {}
    for k in old:
        if k in new_map and str(new_map[k]).strip():
            ordered[k] = new_map[k]
    for k in sorted(new_map):
        if k not in ordered and str(new_map[k]).strip():
            ordered[k] = new_map[k]
    with open(TRANS, "w", encoding="utf-8") as f:
        json.dump(ordered, f, ensure_ascii=False, indent=2)
        f.write("\n")
    return len(ordered)


def run_build():
    """跑 rebuild.ps1 -NoPause，回传 (returncode, 合并日志)。"""
    if game_running():
        return 1, "[X] 检测到游戏 CalaPlayer 正在运行。打包会写入被占用的 .ucas 文件，请先完全关闭游戏再打包。"
    try:
        p = subprocess.run(
            ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass",
             "-File", REBUILD, "-NoPause"],
            capture_output=True, text=True, encoding="utf-8", errors="replace",
            cwd=HERE, timeout=1200,
        )
        log = (p.stdout or "") + (("\n" + p.stderr) if p.stderr else "")
        return p.returncode, log.strip()
    except subprocess.TimeoutExpired:
        return 1, "[X] 打包超时（>20 分钟），已中止。"
    except Exception as e:
        return 1, "[X] 启动打包失败：%s" % e


class Handler(http.server.BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass  # 静默，别刷屏

    def _send(self, code, body, ctype="application/json; charset=utf-8"):
        data = body if isinstance(body, bytes) else body.encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)

    def _json(self, obj, code=200):
        self._send(code, json.dumps(obj, ensure_ascii=False))

    def do_GET(self):
        if self.path in ("/", "/index.html"):
            try:
                with open(INDEX, "rb") as f:
                    self._send(200, f.read(), "text/html; charset=utf-8")
            except Exception as e:
                self._send(500, "index.html 缺失: %s" % e, "text/plain; charset=utf-8")
        elif self.path == "/api/data":
            self._json({
                "translations": load_json(TRANS, {}),
                "reference": load_json(REF, []),
                "names": load_names(),
                "gameRunning": game_running(),
            })
        elif self.path == "/api/status":
            self._json({"gameRunning": game_running()})
        else:
            self._send(404, "not found", "text/plain; charset=utf-8")

    def do_POST(self):
        length = int(self.headers.get("Content-Length", 0))
        raw = self.rfile.read(length) if length else b"{}"
        try:
            payload = json.loads(raw.decode("utf-8"))
        except Exception:
            payload = {}
        if self.path == "/api/save":
            try:
                n = save_translations(payload.get("translations", {}))
                save_names(payload.get("names", {}))
                self._json({"ok": True, "count": n})
            except Exception as e:
                self._json({"ok": False, "error": str(e)}, 500)
        elif self.path == "/api/build":
            # 保存后再打包（前端一般已先调 save，这里兜底一次）
            try:
                if "translations" in payload:
                    save_translations(payload["translations"])
                save_names(payload.get("names", {}))
            except Exception:
                pass
            bad = name_conflicts()
            if bad:
                self._json({"ok": False, "code": 6, "log": "[X] 以下译名重复，请改成不同的中文再打包：\n" + "\n".join(bad)})
                return
            code, log = run_build()
            self._json({"ok": code == 0, "code": code, "log": log})
        else:
            self._send(404, "not found", "text/plain; charset=utf-8")


def pick_port(start=8756, tries=20):
    for p in range(start, start + tries):
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
            if s.connect_ex(("127.0.0.1", p)) != 0:
                return p
    return start


def main():
    for need in (TRANS, REF, INDEX, REBUILD):
        if not os.path.isfile(need):
            print("缺少文件：%s" % need)
            input("按回车退出")
            return
    port = pick_port()
    url = "http://127.0.0.1:%d/" % port
    srv = http.server.ThreadingHTTPServer(("127.0.0.1", port), Handler)
    print("=" * 46)
    print("  CalaPlayer 汉化工具已启动")
    print("  浏览器打开： %s" % url)
    print("  用完直接关掉这个黑窗口即可退出。")
    print("=" * 46)
    threading.Timer(0.6, lambda: webbrowser.open(url)).start()
    try:
        srv.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()

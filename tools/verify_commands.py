#!/usr/bin/env python3
"""Check every command id the plugin sends exists in your Audacity build.
Run with Audacity open and mod-script-pipe enabled:  python3 tools/verify_commands.py"""
import json, os, sys

if sys.platform == "win32":
    TO, FROM, EOL = r"\\.\pipe\ToSrvPipe", r"\\.\pipe\FromSrvPipe", "\r\n\0"
else:
    TO, FROM, EOL = f"/tmp/audacity_script_pipe.to.{os.getuid()}", f"/tmp/audacity_script_pipe.from.{os.getuid()}", "\n"

USED = ["Record1stChoice", "Record2ndChoice", "PlayStop", "Stop", "Pause", "CursProjectStart",
        "CursProjectEnd", "AddLabel", "AddLabelPlaying", "Undo", "Save", "ZoomIn", "ZoomOut",
        "FitInWindow", "SeekLeftShort", "SeekRightShort", "CursorShortJumpLeft",
        "CursorShortJumpRight", "SetTrack", "Message"]

to = open(TO, "w"); frm = open(FROM, "r")

def send(cmd):
    to.write(cmd + EOL); to.flush()
    lines = []
    while True:
        line = frm.readline()
        if line.startswith("BatchCommand finished:"):
            frm.readline()
            return "\n".join(lines), "OK" in line
        lines.append(line.rstrip("\n"))

out, ok = send("GetInfo: Type=Commands Format=JSON")
ids = {c.get("id") for c in json.loads(out)}
missing = [c for c in USED if c not in ids]
print("All command ids found." if not missing else f"MISSING: {missing}")
for c in missing:
    near = sorted(i for i in ids if i and c[:5].lower() in i.lower())
    print(f"  {c}: similar -> {near[:8]}")

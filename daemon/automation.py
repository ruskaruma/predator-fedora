"""
Automatic profiles for the PredatorCore daemon.

One background loop (every 3 s) handles:
  * Power source: on battery switch to a battery mode and dim the keyboard; on AC restore
    the mode that was last used on AC and the keyboard brightness.
  * Gaming: while a Steam game (``reaper SteamLaunch AppId=...``) or a user-listed process is
    running, switch to the gaming mode and fan mode; restore everything when it exits.
  * Heat guard: CPU at or above the limit for 30 s sends a desktop notification and can step
    the mode down one level.
Desktop notifications are delivered into the active graphical session as that user.
"""

import configparser
import glob
import json
import logging
import os
import pwd
import subprocess
import threading
import time
from typing import Dict, List, Optional, Tuple

log = logging.getLogger("DAMXDaemon")

AC_PATHS = ["/sys/class/power_supply/AC/online", "/sys/class/power_supply/ACAD/online",
            "/sys/class/power_supply/ADP1/online", "/sys/class/power_supply/AC0/online"]

# Firmware profile ids and the names shown to the user
PROFILE_NAMES = {"low-power": "Eco", "quiet": "Quiet", "balanced": "Balanced",
                 "balanced-performance": "Performance", "performance": "Turbo"}
AC_CYCLE = ["quiet", "balanced", "balanced-performance", "performance"]
BATTERY_CYCLE = ["low-power", "balanced"]
STEP_DOWN = {"performance": "balanced-performance", "balanced-performance": "balanced", "balanced": "quiet"}

DEFAULTS = {
    "battery_enabled": True,
    "battery_profile": "low-power",
    "battery_dim_keyboard": True,
    "battery_keyboard_brightness": 30,
    "ac_restore_profile": True,
    "game_enabled": True,
    "game_profile": "performance",
    "game_fans": "keep",          # keep | max | curve
    "game_processes": "",          # extra process names, comma separated
    "heat_guard_enabled": True,
    "heat_guard_temp": 95,
    "heat_guard_step_down": False,
    "notify_mode_changes": True,
}

INTERVAL = 3.0
HEAT_GUARD_SECONDS = 30
HEAT_NOTIFY_COOLDOWN = 600


def _read(path: str) -> Optional[str]:
    try:
        with open(path) as f:
            return f.read().strip()
    except OSError:
        return None


class Notifier:
    """Sends desktop notifications into the active graphical session."""

    def _session_user(self) -> Optional[Tuple[str, int]]:
        try:
            out = subprocess.run(["loginctl", "list-sessions", "--no-legend"], capture_output=True, text=True,
                                 timeout=3).stdout
        except (OSError, subprocess.SubprocessError):
            return None
        for line in out.splitlines():
            parts = line.split()
            if len(parts) >= 4 and parts[3] == "seat0":  # the local graphical seat
                try:
                    return parts[2], pwd.getpwnam(parts[2]).pw_uid
                except KeyError:
                    continue
        return None

    def send(self, title: str, body: str = "", urgent: bool = False):
        user = self._session_user()
        if not user:
            return
        name, uid = user
        cmd = ["runuser", "-u", name, "--", "env", f"DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/{uid}/bus",
               "notify-send", "-a", "PredatorCore", "-i", "predatorcore", "-t", "2500",
               # same tag replaces the previous popup instead of stacking them (OSD-like)
               "-h", "string:x-canonical-private-synchronous:predatorcore",
               "-u", "critical" if urgent else "normal", title]
        if body:
            cmd.append(body)
        try:
            subprocess.run(cmd, capture_output=True, timeout=5)
        except (OSError, subprocess.SubprocessError) as e:
            log.debug(f"Notification failed: {e}")


class AutomationController:
    def __init__(self, manager, config: configparser.ConfigParser, save_config):
        self.manager = manager
        self.config = config
        self.save_config = save_config
        self.notifier = Notifier()
        self.settings = self._load()
        self.state = self._load_state()

        self._on_ac: Optional[bool] = None
        self._game: Optional[str] = None
        self._hot_since: Optional[float] = None
        self._last_heat_notice = 0.0
        self._cpu_temp_path = self._find_cpu_temp()
        self._stop = threading.Event()
        self._thread: Optional[threading.Thread] = None
        self._lock = threading.RLock()

    # ---------- settings ----------

    def _load(self) -> Dict:
        section = self.config["Automation"] if self.config.has_section("Automation") else {}
        out = {}
        for key, default in DEFAULTS.items():
            raw = section.get(key)
            if raw is None:
                out[key] = default
            elif isinstance(default, bool):
                out[key] = str(raw).lower() == "true"
            elif isinstance(default, int):
                out[key] = int(raw) if str(raw).lstrip("-").isdigit() else default
            else:
                out[key] = raw
        return out

    def _load_state(self) -> Dict:
        section = self.config["AutomationState"] if self.config.has_section("AutomationState") else {}
        return {"ac_profile": section.get("ac_profile", ""), "kb_brightness": section.get("kb_brightness", "")}

    def _persist(self):
        self.config["Automation"] = {k: str(v) for k, v in self.settings.items()}
        self.config["AutomationState"] = {k: str(v) for k, v in self.state.items()}
        self.save_config()

    def status(self) -> Dict:
        with self._lock:
            return dict(self.settings, on_ac=self._on_ac, active_game=self._game or "")

    def configure(self, changes: Dict) -> Tuple[bool, str]:
        with self._lock:
            for key, value in changes.items():
                if key not in DEFAULTS:
                    return False, f"Unknown setting: {key}"
                default = DEFAULTS[key]
                if isinstance(default, bool):
                    value = bool(value)
                elif isinstance(default, int):
                    value = int(value)
                else:
                    value = str(value)
                if key in ("battery_profile", "game_profile") and value not in PROFILE_NAMES:
                    return False, f"Unknown mode: {value}"
                if key == "game_fans" and value not in ("keep", "max", "curve"):
                    return False, "game_fans must be keep, max or curve"
                if key == "battery_keyboard_brightness" and not 0 <= value <= 100:
                    return False, "Brightness must be 0-100"
                if key == "heat_guard_temp" and not 80 <= value <= 100:
                    return False, "Heat guard temperature must be 80-100 °C"
                self.settings[key] = value
            self._persist()
        return True, ""

    # ---------- public actions ----------

    def cycle_profile(self) -> Tuple[bool, str]:
        """Next mode in the cycle for the current power source (used by the hotkey)."""
        choices = self.manager.get_thermal_profile_choices()
        cycle = [p for p in (AC_CYCLE if self._ac_connected() else BATTERY_CYCLE) if p in choices]
        if not cycle:
            return False, "No modes available"
        current = self.manager.get_thermal_profile()
        nxt = cycle[(cycle.index(current) + 1) % len(cycle)] if current in cycle else cycle[0]
        if not self.manager.set_thermal_profile(nxt):
            return False, "Failed to set mode"
        if self._ac_connected():
            self.state["ac_profile"] = nxt
            self._persist()
        self._announce(nxt, "Hotkey", force=True)
        return True, nxt

    def remember_user_profile(self, profile: str):
        """Called when the user picks a mode in the GUI, so AC restore returns to it."""
        if self._ac_connected() and not self._game:
            self.state["ac_profile"] = profile
            self._persist()

    def start(self):
        self._thread = threading.Thread(target=self._loop, name="automation", daemon=True)
        self._thread.start()

    def stop(self):
        self._stop.set()
        if self._thread:
            self._thread.join(timeout=5)

    # ---------- loop ----------

    def _loop(self):
        while not self._stop.is_set():
            try:
                with self._lock:
                    self._check_power()
                    self._check_game()
                    self._check_heat()
            except Exception as e:
                log.error(f"Automation loop error: {e}")
            self._stop.wait(INTERVAL)

    def _check_power(self):
        on_ac = self._ac_connected()
        if on_ac == self._on_ac:
            return
        first = self._on_ac is None
        self._on_ac = on_ac
        if first and on_ac:
            return  # daemon start on AC: nothing to restore
        if self._game:
            return  # gaming rules own the mode until the game exits

        if not on_ac and self.settings["battery_enabled"]:
            current = self.manager.get_thermal_profile()
            if current and current != self.settings["battery_profile"]:
                self.state["ac_profile"] = current
            self._set_profile(self.settings["battery_profile"], "On battery")
            if self.settings["battery_dim_keyboard"]:
                self._dim_keyboard(self.settings["battery_keyboard_brightness"])
            self._persist()
        elif on_ac and not first:
            if self.settings["ac_restore_profile"] and self.state.get("ac_profile"):
                self._set_profile(self.state["ac_profile"], "Plugged in")
            self._restore_keyboard()
            self._persist()

    def _check_game(self):
        if not self.settings["game_enabled"]:
            if self._game:
                self._end_game()
            return
        game = self._find_game()
        if game and not self._game:
            self._game = game
            self.state["pre_game_profile"] = self.manager.get_thermal_profile()
            cpu, gpu = self.manager.get_fan_speed()
            self.state["pre_game_fans"] = f"{cpu},{gpu}"
            fan_curve = getattr(self.manager, "fan_curve", None)
            self.state["pre_game_curve"] = str(bool(fan_curve and fan_curve.enabled))
            self._set_profile(self.settings["game_profile"], f"Game detected: {game}", force=True)
            if self.settings["game_fans"] == "max":
                if fan_curve:
                    fan_curve.disable_for_manual_control()
                self.manager.set_fan_speed(100, 100)
            elif self.settings["game_fans"] == "curve" and fan_curve:
                fan_curve.configure(True)
        elif not game and self._game:
            self._end_game()

    def _end_game(self):
        game, self._game = self._game, None
        profile = self.state.pop("pre_game_profile", "")
        fans = self.state.pop("pre_game_fans", "0,0")
        curve_was_on = self.state.pop("pre_game_curve", "False") == "True"
        fan_curve = getattr(self.manager, "fan_curve", None)
        if self.settings["game_fans"] != "keep":
            if curve_was_on and fan_curve:
                fan_curve.configure(True)
            else:
                if fan_curve and self.settings["game_fans"] == "curve":
                    fan_curve.configure(False)
                cpu, gpu = (int(x) for x in fans.split(","))
                self.manager.set_fan_speed(cpu, gpu)
        if profile:
            self._set_profile(profile, f"{game} closed", force=True)

    def _check_heat(self):
        if not self.settings["heat_guard_enabled"]:
            self._hot_since = None
            return
        temp = self._cpu_temp()
        if temp is None or temp < self.settings["heat_guard_temp"]:
            self._hot_since = None
            return
        now = time.monotonic()
        if self._hot_since is None:
            self._hot_since = now
            return
        if now - self._hot_since < HEAT_GUARD_SECONDS:
            return

        self._hot_since = now  # re-arm: act at most once per sustained window
        stepped = ""
        if self.settings["heat_guard_step_down"]:
            current = self.manager.get_thermal_profile()
            lower = STEP_DOWN.get(current)
            if lower and self.manager.set_thermal_profile(lower):
                stepped = f" Switched to {PROFILE_NAMES[lower]}."
        if now - self._last_heat_notice >= HEAT_NOTIFY_COOLDOWN or stepped:
            self._last_heat_notice = now
            self.notifier.send(f"CPU at {temp:.0f}°C for {HEAT_GUARD_SECONDS} s",
                               f"Sustained high temperature.{stepped} Check the Monitoring page for the cause.",
                               urgent=True)

    # ---------- helpers ----------

    def _set_profile(self, profile: str, reason: str, force: bool = False):
        if profile not in self.manager.get_thermal_profile_choices():
            return
        if self.manager.get_thermal_profile() == profile and not force:
            return
        if self.manager.set_thermal_profile(profile):
            log.info(f"Automation: {reason} -> {profile}")
            self._announce(profile, reason)

    def _announce(self, profile: str, reason: str, force: bool = False):
        if self.settings["notify_mode_changes"] or force:
            self.notifier.send(f"{PROFILE_NAMES.get(profile, profile)} mode", reason)

    def _find_game(self) -> Optional[str]:
        extra = [p.strip().lower() for p in self.settings["game_processes"].split(",") if p.strip()]
        for pid_dir in glob.glob("/proc/[0-9]*"):
            cmd = _read(f"{pid_dir}/cmdline")
            if not cmd:
                continue
            args = cmd.split("\0")
            if "SteamLaunch" in args and any(a.startswith("AppId=") for a in args):
                app = next(a for a in args if a.startswith("AppId="))
                return f"Steam game ({app})"
            if extra:
                exe = os.path.basename(args[0]).lower()
                if exe in extra:
                    return exe
        return None

    def _dim_keyboard(self, brightness: int):
        """Re-apply whichever keyboard mode is active with a lower brightness, keeping effects running."""
        four = self.manager.get_four_zone_mode() if "four_zone_mode" in self.manager.available_features else ""
        zones = self.manager.get_per_zone_mode() if "per_zone_mode" in self.manager.available_features else ""
        f = four.split(",") if four else []
        if len(f) == 7 and f[0] != "0":
            self.state["kb_brightness"] = f"four:{f[2]}"
            self.manager.set_four_zone_mode(int(f[0]), int(f[1]), brightness, int(f[3]), int(f[4]), int(f[5]),
                                            int(f[6]))
        elif zones and len(zones.split(",")) == 5:
            z = zones.split(",")
            self.state["kb_brightness"] = f"zones:{z[4]}"
            self.manager.set_per_zone_mode(z[0], z[1], z[2], z[3], brightness)

    def _restore_keyboard(self):
        saved = self.state.get("kb_brightness", "")
        if not saved or ":" not in saved:
            return
        kind, value = saved.split(":", 1)
        self.state["kb_brightness"] = ""
        if kind == "four":
            f = (self.manager.get_four_zone_mode() or "").split(",")
            if len(f) == 7:
                self.manager.set_four_zone_mode(int(f[0]), int(f[1]), int(value), int(f[3]), int(f[4]), int(f[5]),
                                                int(f[6]))
        elif kind == "zones":
            z = (self.manager.get_per_zone_mode() or "").split(",")
            if len(z) == 5:
                self.manager.set_per_zone_mode(z[0], z[1], z[2], z[3], int(value))

    @staticmethod
    def _ac_connected() -> bool:
        for path in AC_PATHS:
            value = _read(path)
            if value is not None:
                return value == "1"
        return True

    @staticmethod
    def _find_cpu_temp() -> Optional[str]:
        for hwmon in glob.glob("/sys/class/hwmon/hwmon*"):
            if _read(f"{hwmon}/name") == "coretemp":
                return f"{hwmon}/temp1_input"
        return None

    def _cpu_temp(self) -> Optional[float]:
        raw = _read(self._cpu_temp_path) if self._cpu_temp_path else None
        return int(raw) / 1000 if raw and raw.isdigit() else None

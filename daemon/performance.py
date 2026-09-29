"""
CPU power limits and custom fan curves for the PredatorCore daemon.

Power limits
------------
Intel RAPL exposes two independent sets of package limits:

  * MSR  (/sys/class/powercap/intel-rapl:0)       - set by the OS/BIOS at boot
  * MMIO (/sys/class/powercap/intel-rapl-mmio:0)  - rewritten by the Acer
    firmware every time the platform profile (Quiet/Balanced/...) changes

The CPU obeys the lower of the two. On the PHN16-71 the firmware sets MMIO PL1
to 45/70/80/100 W per profile, MSR PL1 is 65 W, and PL2 is 157 W everywhere.
A user override therefore writes both interfaces and is re-applied whenever
the firmware changes them again.

Fan curves
----------
A background thread reads the CPU package temperature (and the NVIDIA GPU
temperature while the dGPU is awake), interpolates a user curve and writes the
Linuwu-Sense fan_speed attribute. A curve value of 0 hands that fan back to
the firmware's automatic control.
"""

import configparser
import glob
import json
import logging
import os
import subprocess
import threading
from typing import Dict, List, Optional, Tuple

log = logging.getLogger("DAMXDaemon")

RAPL_MSR = "/sys/class/powercap/intel-rapl:0"
RAPL_MMIO = "/sys/class/powercap/intel-rapl-mmio:0"
PLATFORM_PROFILE = "/sys/firmware/acpi/platform_profile"
STOCK_FILE = "/etc/DAMX_Daemon/stock_power_limits.json"

PL1_RANGE = (15, 115)   # watts
PL2_MAX = 157           # never allow bursts above the factory limit

DEFAULT_CPU_CURVE = [[50, 0], [65, 35], [75, 55], [85, 80], [92, 100]]
DEFAULT_GPU_CURVE = [[50, 0], [65, 35], [72, 55], [80, 80], [87, 100]]
SAFETY_TEMP = 95        # force 100% above this, whatever the curve says
HYSTERESIS = 3          # degrees C before a fan is allowed to slow down
MIN_STEP = 3            # ignore changes smaller than this many percent
INTERVAL = 2.0          # seconds between curve evaluations


def _read(path: str) -> Optional[str]:
    try:
        with open(path) as f:
            return f.read().strip()
    except OSError:
        return None


def _write(path: str, value: str) -> bool:
    try:
        with open(path, "w") as f:
            f.write(value)
        return True
    except OSError as e:
        log.error(f"Failed to write {path}: {e}")
        return False


def _read_watts(base: str, constraint: int) -> Optional[int]:
    raw = _read(f"{base}/constraint_{constraint}_power_limit_uw")
    return round(int(raw) / 1_000_000) if raw and raw.isdigit() else None


def _write_watts(base: str, constraint: int, watts: int) -> bool:
    return _write(f"{base}/constraint_{constraint}_power_limit_uw", str(int(watts) * 1_000_000))


class PowerLimitController:
    """Reads, overrides and restores CPU package power limits (PL1/PL2)."""

    def __init__(self, config: configparser.ConfigParser, save_config):
        self.config = config
        self.save_config = save_config
        self.available = os.path.exists(f"{RAPL_MSR}/constraint_0_power_limit_uw")
        self.has_mmio = os.path.exists(f"{RAPL_MMIO}/constraint_0_power_limit_uw")
        self.stock = self._load_or_capture_stock() if self.available else {}

        section = self.config["PowerLimits"] if self.config.has_section("PowerLimits") else {}
        self.enabled = str(section.get("Enabled", "False")).lower() == "true"
        self.pl1 = int(section.get("PL1", self.stock.get("msr_pl1", 65)))
        self.pl2 = int(section.get("PL2", self.stock.get("msr_pl2", 157)))

    def _load_or_capture_stock(self) -> Dict[str, int]:
        """Stock MSR limits are captured once, before we ever write them, so Reset is truly stock."""
        try:
            with open(STOCK_FILE) as f:
                return json.load(f)
        except (OSError, ValueError):
            pass
        stock = {"msr_pl1": _read_watts(RAPL_MSR, 0) or 65, "msr_pl2": _read_watts(RAPL_MSR, 1) or 157}
        try:
            os.makedirs(os.path.dirname(STOCK_FILE), exist_ok=True)
            with open(STOCK_FILE, "w") as f:
                json.dump(stock, f)
            log.info(f"Captured stock power limits: {stock}")
        except OSError as e:
            log.error(f"Could not persist stock power limits: {e}")
        return stock

    def status(self) -> Dict:
        msr = (_read_watts(RAPL_MSR, 0), _read_watts(RAPL_MSR, 1))
        mmio = (_read_watts(RAPL_MMIO, 0), _read_watts(RAPL_MMIO, 1)) if self.has_mmio else (None, None)
        effective_pl1 = min(v for v in (msr[0], mmio[0]) if v is not None) if msr[0] else None
        effective_pl2 = min(v for v in (msr[1], mmio[1]) if v is not None) if msr[1] else None
        return {
            "enabled": self.enabled,
            "pl1": self.pl1,
            "pl2": self.pl2,
            "effective_pl1": effective_pl1,
            "effective_pl2": effective_pl2,
            "firmware_pl1": mmio[0],
            "stock_pl1": self.stock.get("msr_pl1"),
            "stock_pl2": self.stock.get("msr_pl2"),
            "pl1_min": PL1_RANGE[0],
            "pl1_max": PL1_RANGE[1],
            "pl2_max": PL2_MAX,
        }

    def set_limits(self, pl1: int, pl2: int) -> Tuple[bool, str]:
        if not self.available:
            return False, "RAPL power limits are not available"
        pl1, pl2 = int(pl1), int(pl2)
        if not PL1_RANGE[0] <= pl1 <= PL1_RANGE[1]:
            return False, f"PL1 must be between {PL1_RANGE[0]} and {PL1_RANGE[1]} W"
        if not pl1 <= pl2 <= PL2_MAX:
            return False, f"PL2 must be between PL1 ({pl1} W) and {PL2_MAX} W"

        self.enabled, self.pl1, self.pl2 = True, pl1, pl2
        self._persist()
        return (True, "") if self.apply() else (False, "Failed to write power limits")

    def reset(self) -> Tuple[bool, str]:
        """Restore stock MSR limits and let the firmware re-apply its per-profile MMIO limits."""
        if not self.available:
            return False, "RAPL power limits are not available"
        self.enabled = False
        self._persist()
        ok = _write_watts(RAPL_MSR, 0, self.stock.get("msr_pl1", 65))
        ok &= _write_watts(RAPL_MSR, 1, self.stock.get("msr_pl2", 157))
        profile = _read(PLATFORM_PROFILE)
        if profile:
            _write(PLATFORM_PROFILE, profile)  # re-selecting the profile makes the firmware rewrite MMIO
        return (True, "") if ok else (False, "Failed to restore stock limits")

    def apply(self) -> bool:
        if not (self.available and self.enabled):
            return True
        ok = _write_watts(RAPL_MSR, 0, self.pl1) and _write_watts(RAPL_MSR, 1, self.pl2)
        if self.has_mmio:
            ok &= _write_watts(RAPL_MMIO, 0, self.pl1) and _write_watts(RAPL_MMIO, 1, self.pl2)
        if ok:
            log.info(f"Applied power limits PL1={self.pl1}W PL2={self.pl2}W")
        return ok

    def enforce(self):
        """Called periodically: the firmware rewrites MMIO limits on every profile change."""
        if not (self.available and self.enabled):
            return
        wanted = (self.pl1, self.pl2)
        current = [(_read_watts(RAPL_MSR, 0), _read_watts(RAPL_MSR, 1))]
        if self.has_mmio:
            current.append((_read_watts(RAPL_MMIO, 0), _read_watts(RAPL_MMIO, 1)))
        if any(c != wanted for c in current):
            self.apply()

    def _persist(self):
        self.config["PowerLimits"] = {"Enabled": str(self.enabled), "PL1": str(self.pl1), "PL2": str(self.pl2)}
        self.save_config()


class FanCurveController:
    """Drives fan_speed from temperature curves on a background thread."""

    def __init__(self, fan_speed_path: str, config: configparser.ConfigParser, save_config,
                 power_limits: Optional[PowerLimitController] = None):
        self.fan_speed_path = fan_speed_path
        self.config = config
        self.save_config = save_config
        self.power_limits = power_limits
        self.available = os.path.exists(fan_speed_path)
        self.cpu_temp_path = self._find_cpu_temp()
        self.nvidia_pci = self._find_nvidia_pci()

        section = self.config["FanCurve"] if self.config.has_section("FanCurve") else {}
        self.enabled = str(section.get("Enabled", "False")).lower() == "true"
        self.cpu_curve = self._parse(section.get("CPU"), DEFAULT_CPU_CURVE)
        self.gpu_curve = self._parse(section.get("GPU"), DEFAULT_GPU_CURVE)

        self._current = (-1, -1)
        self._last_temps = (None, None)
        self._stop = threading.Event()
        self._thread: Optional[threading.Thread] = None
        self._lock = threading.Lock()

    # ---------- public API ----------

    def start(self):
        if self._thread and self._thread.is_alive():
            return
        self._stop.clear()
        self._thread = threading.Thread(target=self._loop, name="fan-curve", daemon=True)
        self._thread.start()

    def stop(self):
        self._stop.set()
        if self._thread:
            self._thread.join(timeout=5)

    def status(self) -> Dict:
        return {
            "enabled": self.enabled,
            "cpu": self.cpu_curve,
            "gpu": self.gpu_curve,
            "cpu_temp": self._last_temps[0],
            "gpu_temp": self._last_temps[1],
            "cpu_percent": max(self._current[0], 0),
            "gpu_percent": max(self._current[1], 0),
            "safety_temp": SAFETY_TEMP,
        }

    def configure(self, enabled: bool, cpu_curve=None, gpu_curve=None) -> Tuple[bool, str]:
        if not self.available:
            return False, "Fan control is not available"
        try:
            cpu = self._validate(cpu_curve) if cpu_curve is not None else self.cpu_curve
            gpu = self._validate(gpu_curve) if gpu_curve is not None else self.gpu_curve
        except ValueError as e:
            return False, str(e)

        with self._lock:
            was_enabled = self.enabled
            self.enabled, self.cpu_curve, self.gpu_curve = bool(enabled), cpu, gpu
            self._current = (-1, -1)  # force a write on the next tick
        self._persist()
        if was_enabled and not enabled:
            _write(self.fan_speed_path, "0,0")  # hand control back to the firmware
        return True, ""

    def disable_for_manual_control(self):
        """A manual Auto/Max/Custom choice in the GUI takes precedence over the curve."""
        if self.enabled:
            with self._lock:
                self.enabled = False
            self._persist()
            log.info("Fan curve disabled by manual fan speed change")

    # ---------- internals ----------

    def _loop(self):
        while not self._stop.wait(INTERVAL):
            try:
                if self.power_limits:
                    self.power_limits.enforce()
                with self._lock:
                    enabled = self.enabled
                if enabled:
                    self._tick()
            except Exception as e:  # never let the thread die
                log.error(f"Fan curve loop error: {e}")

    def _tick(self):
        cpu_temp = self._cpu_temp()
        gpu_temp = self._gpu_temp()
        self._last_temps = (cpu_temp, gpu_temp)
        if cpu_temp is None:
            return
        # With the dGPU asleep the GPU fan still cools the shared heat pipes, so it follows the CPU.
        gpu_basis = gpu_temp if gpu_temp is not None else cpu_temp

        cpu_pct = self._next(self._current[0], cpu_temp, self.cpu_curve)
        gpu_pct = self._next(self._current[1], gpu_basis, self.gpu_curve)

        if max(cpu_temp, gpu_temp or 0) >= SAFETY_TEMP:
            cpu_pct = gpu_pct = 100

        if (cpu_pct, gpu_pct) != self._current:
            if _write(self.fan_speed_path, f"{cpu_pct},{gpu_pct}"):
                self._current = (cpu_pct, gpu_pct)

    @staticmethod
    def _interpolate(curve: List[List[int]], temp: float) -> int:
        if temp <= curve[0][0]:
            return curve[0][1]
        for (t0, p0), (t1, p1) in zip(curve, curve[1:]):
            if temp <= t1:
                return round(p0 + (p1 - p0) * (temp - t0) / (t1 - t0))
        return curve[-1][1]

    def _next(self, current: int, temp: float, curve) -> int:
        rising = self._interpolate(curve, temp)
        if current < 0:
            return rising
        if rising > current:
            return rising if rising - current >= MIN_STEP or rising == 100 else current
        # Only slow down once the temperature has dropped HYSTERESIS degrees below the curve point.
        falling = self._interpolate(curve, temp + HYSTERESIS)
        if falling < current and (current - falling >= MIN_STEP or falling == 0):
            return falling
        return current

    @staticmethod
    def _validate(curve) -> List[List[int]]:
        if not isinstance(curve, list) or not 2 <= len(curve) <= 10:
            raise ValueError("A curve needs 2 to 10 points")
        points = []
        for point in curve:
            t, p = int(point[0]), int(point[1])
            if not (20 <= t <= 100 and 0 <= p <= 100):
                raise ValueError("Curve points must be 20-100 °C and 0-100 %")
            points.append([t, p])
        points.sort(key=lambda x: x[0])
        if any(b[0] <= a[0] for a, b in zip(points, points[1:])):
            raise ValueError("Curve temperatures must be distinct")
        if any(b[1] < a[1] for a, b in zip(points, points[1:])):
            raise ValueError("Fan speed must not decrease as temperature rises")
        return points

    @classmethod
    def _parse(cls, raw: Optional[str], default) -> List[List[int]]:
        try:
            return cls._validate(json.loads(raw)) if raw else [p[:] for p in default]
        except (ValueError, TypeError):
            return [p[:] for p in default]

    def _persist(self):
        self.config["FanCurve"] = {
            "Enabled": str(self.enabled),
            "CPU": json.dumps(self.cpu_curve),
            "GPU": json.dumps(self.gpu_curve),
        }
        self.save_config()

    @staticmethod
    def _find_cpu_temp() -> Optional[str]:
        for hwmon in glob.glob("/sys/class/hwmon/hwmon*"):
            if _read(f"{hwmon}/name") == "coretemp" and os.path.exists(f"{hwmon}/temp1_input"):
                return f"{hwmon}/temp1_input"
        return None

    @staticmethod
    def _find_nvidia_pci() -> Optional[str]:
        for dev in glob.glob("/sys/bus/pci/devices/*"):
            if _read(f"{dev}/vendor") == "0x10de" and (_read(f"{dev}/class") or "").startswith("0x03"):
                return dev
        return None

    def _cpu_temp(self) -> Optional[float]:
        raw = _read(self.cpu_temp_path) if self.cpu_temp_path else None
        return int(raw) / 1000 if raw and raw.lstrip("-").isdigit() else None

    def _gpu_temp(self) -> Optional[float]:
        if not self.nvidia_pci or _read(f"{self.nvidia_pci}/power/runtime_status") != "active":
            return None  # never wake a suspended dGPU just to read it
        try:
            out = subprocess.run(["nvidia-smi", "--query-gpu=temperature.gpu", "--format=csv,noheader,nounits"],
                                 capture_output=True, text=True, timeout=2).stdout.strip()
            return float(out.splitlines()[0]) if out else None
        except (OSError, ValueError, subprocess.SubprocessError, IndexError):
            return None

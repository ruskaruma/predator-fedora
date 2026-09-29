# PredatorCore

A PredatorSense-style control center for Acer Predator laptops on Linux. It covers performance modes, fan control, 4-zone RGB lighting, battery care and live hardware monitoring.

Built and tuned on an **Acer Predator Helios Neo 16 (PHN16-71)** running Fedora 44 (i9-13900HX + RTX 4060 laptop GPU), but it should work on any Predator or Nitro laptop supported by the [Linuwu-Sense](https://github.com/0x7375646F/Linuwu-Sense) driver.

## Features

- **Monitoring**: a thermal-throttling indicator, the top processes heating the laptop (with an End button), what's keeping the NVIDIA GPU awake, CPU and GPU temperature and load gauges, average and peak clock, CPU package power, GPU clock, board power, VRAM and power state, fan RPM and duty, 2-minute temperature history, RAM and swap, disk and NVMe temperature, network throughput, and battery health, cycle count, draw and time left.
- **Performance**: Eco, Quiet, Balanced, Performance and Turbo modes.
- **CPU power limits**: set the sustained (PL1) and burst (PL2) package power. Your limits apply on top of every mode and are re-applied after mode changes, sleep and reboot. The stock limits allow 157 W bursts in *every* mode, which is what sends the i9 to 100 °C on light loads.
- **Fans**: the firmware's Auto curve, Max (about 7400 RPM on the PHN16-71), fixed custom speeds, or **custom temperature curves** for the CPU and GPU fans. Curves are edited by dragging points, and a 95 °C safety override always applies.
- **Lighting**: per-zone static colours with a live keyboard preview, brightness, hardware effects (breathing, wave, neon, …) and backlight timeout.
- **Battery**: 80 % health-mode charge limit, calibration, and USB charging while powered off.
- **Settings**: LCD overdrive and the boot animation and sound.

### Built to go easy on the hardware

- No decorative animations. Fan graphics are static and only redraw when a value changes, and chart transitions are off. Idle CPU use is about 3–5 % of one core.
- Sensors are polled only while the Monitoring page is open and the window isn't minimised.
- The NVIDIA GPU is never woken up just to read its sensors. When it's runtime-suspended, the app shows it as *Sleeping* and doesn't run `nvidia-smi`. While it's awake, it's read at most every 15 s, so the driver's ~9 s idle timer can still put it back to sleep.
- **GPU power** switch: *Auto sleep* lets the RTX 4060 power down whenever nothing needs it; *Always on* keeps it ready for games and CUDA.
- CPU usage, package power and network rates are worked out from the change since the previous sample, so an update never has to pause to take a measurement.
- A single `nvidia-smi` call per refresh reads all GPU fields at once.
- Battery maths handles both `energy_*` and `charge_*` sysfs batteries. The PHN16-71 reports `charge_*`.

## Requirements

PredatorCore is the app. It needs two things underneath it:

1. The **Linuwu-Sense** kernel driver, which replaces `acer_wmi` and exposes fan, RGB and battery controls.
2. The **control daemon**, a root service that the app talks to over `/var/run/DAMX.sock`. Its source is in [`daemon/`](daemon).

The simplest way to get both is to install the upstream DAMX suite once (see Credits). PredatorCore then replaces only the app.

Package power (RAPL) is readable only by root by default. To see it, let the `linuwu_sense` group read it:

```bash
echo 'z /sys/class/powercap/intel-rapl:0/energy_uj 0440 root linuwu_sense' | sudo tee /etc/tmpfiles.d/predatorcore-rapl.conf
sudo systemd-tmpfiles --create /etc/tmpfiles.d/predatorcore-rapl.conf
```

## Download

Get the latest `predatorcore-*-linux-x64.tar.gz` from [Releases](https://github.com/ruskaruma/predator-fedora/releases). It's self-contained, so no .NET install is needed:

```bash
tar xzf predatorcore-*-linux-x64.tar.gz && cd predatorcore-*-linux-x64
./install.sh                 # the app
sudo ./install-daemon.sh     # power limits, fan curves, GPU power
```

## Build and install from source

```bash
# Fedora: sudo dnf install dotnet-sdk-9.0
./scripts/install.sh
```

This builds a self-contained binary and installs it to `/opt/predatorcore`, together with a desktop entry and the `predatorcore` command.

Power limits and fan curves need the PredatorCore daemon. The installer below runs it in place of an existing DAMX daemon, using a systemd drop-in that is easy to undo:

```bash
sudo ./scripts/install-daemon.sh
```

To use your own artwork on your machine only, put `icon.png`, `iconTransparent.png` and/or a `hicolor/` tree in `~/.local/share/predatorcore/icons/`. `python3 assets/make_icon.py <dir> --emblem logo.png` renders any logo with the same glass finish.

For development:

```bash
dotnet run --project src/PredatorCore
```

## Credits

PredatorCore is a derivative of **[Div Acer Manager Max (DAMX)](https://github.com/PXDiv/Div-Acer-Manager-Max) by PXDiv**, licensed under GPL-3.0. The daemon protocol, the daemon itself and much of the application logic come from that project. See [NOTICE](NOTICE) for what was changed.

Hardware access is provided by **[Linuwu-Sense](https://github.com/0x7375646F/Linuwu-Sense)** (GPL-3.0), a reverse-engineered PredatorSense driver.

Fonts: [Oxanium](https://github.com/sevmeyer/oxanium) and [Open Sans](https://github.com/googlefonts/opensans), both under the SIL Open Font License 1.1.

PredatorCore is not affiliated with or endorsed by Acer. "Predator" and "PredatorSense" are trademarks of Acer Inc.

## License

GPL-3.0. See [LICENSE](LICENSE).

# Changelog

## v0.1.0 — first release

PredatorCore is a PredatorSense-style control center for Acer Predator laptops on Linux. It's built and tuned on the Predator Helios Neo 16 (PHN16-71) running Fedora.

### Highlights
- **CPU power limits (PL1/PL2).** The stock firmware allows 157 W bursts in every mode, which sends the i9 to 100 °C on light workloads. Set your own sustained and burst budget. It's re-applied automatically after mode changes, sleep and reboot.
- **Custom fan curves.** Drag-to-edit temperature → speed curves for the CPU and GPU fans, with hysteresis and a 95 °C safety override. You can still pick the firmware's Auto mode, Max, or fixed speeds.
- **GPU power.** Auto sleep powers the RTX 4060 down whenever nothing needs it; Always on keeps it ready for games and CUDA. The app never wakes a sleeping GPU just to read it.
- **What's heating your laptop.** Top processes by CPU, with a safe End button, plus a thermal-throttling indicator.
- **Monitoring.** Temperature and load gauges, clocks, power draw, fan duty, a right-anchored 2-minute temperature history, memory, disk, NVMe temperature, network, and battery health, cycles and draw.
- **Lighting.** 4-zone RGB with a live keyboard preview, hardware effects, brightness and backlight timeout.
- **Battery.** 80 % health-mode limit, calibration, and USB charging while powered off.
- **Design.** Glass-style translucent UI with no decorative animations. Idle CPU use is about 3–5 % of one core.

### Install
```bash
tar xzf predatorcore-0.1.0-linux-x64.tar.gz
cd predatorcore-0.1.0-linux-x64
./install.sh                 # the app
sudo ./install-daemon.sh     # power limits, fan curves, GPU power (needs DAMX + Linuwu-Sense)
```
The download is self-contained, so no .NET install is needed.

### Requirements
- The [Linuwu-Sense](https://github.com/0x7375646F/Linuwu-Sense) kernel driver and the DAMX control daemon (install the upstream DAMX suite once).
- Linux x86-64 with systemd and python3 (for the daemon).

### Credits
Based on [Div Acer Manager Max](https://github.com/PXDiv/Div-Acer-Manager-Max) by PXDiv (GPL-3.0). See NOTICE for the full list of changes. Not affiliated with Acer.

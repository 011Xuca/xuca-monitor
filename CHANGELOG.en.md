# Changelog

Everything that changed in each version of **Pulso**. The newest is on top. (Spanish version: [CHANGELOG.md](CHANGELOG.md).)

## v1.0.4

### New
- **Session recording** (new **Session** tab): pick 1, 5, 10, 15 or 30 minutes and use the PC or play as usual; it is not a synthetic benchmark. When it ends it leaves a report (saved in `Documents\Pulso\benchmarks`, with a row in `benchmarks.csv`, and compared with the previous session of the same app) with average FPS, 1% and 0.1% low, frametimes, stutters, temperatures and CPU and GPU load, VRAM, RAM and paging, and:
  - **Bottleneck**: crosses CPU load against GPU load, VRAM, available RAM, the disk type and the active resolution.
  - **System diagnosis**: Windows edition (full or stripped down), recent critical errors from the event viewer (with `sfc /scannow` and `DISM` to repair), extra processes, services and startup programs (OneDrive, Widgets, telemetry), days without a reboot, full Windows 11 with 8 GB or less, memory reserved by integrated graphics (lower it to 512 MB in the BIOS, different for laptops and desktops), single channel and XMP, and paging on an HDD.
  - **What next**: when **Brío** is worth using depending on what it found (or a congratulation if you are stable with headroom), with a button to open or download it.
- **Network**: live latency at the top of the tab: ping to the internet, 10 s average, variation (jitter), lost packets and ping to the router.
- **Disks**: type and bus of each disk (NVMe, SATA SSD, HDD, USB), SMART health according to Windows and live read and write speed. It warns if Windows flags a failing disk.
- **Memory**: live paging (pages per second read from disk and paging file usage).
- Ready for the **Microsoft Store** (MSIX) and **winget**, with a GitHub Actions workflow that publishes each version.
- The app was renamed from Xuca Monitor to **Pulso** (`Pulso.exe`, repo `github.com/011Xuca/pulso`); your data and "Start with Windows" move over by themselves the first time it opens.
- **Checks for updates on startup**: once per launch, with no background checks. If there is a new version, the yellow update banner appears.
- **More real data without installing anything** (without PawnIO; new `src/SinDriver.cs`):
  - **NVMe disks**: temperature, warning and critical temperature, lifetime, data written and power-on hours are requested **straight from the disk** (`IOCTL_STORAGE_QUERY_PROPERTY`: the NVMe SMART/Health log, the controller Identify and the temperature Windows exposes). It uses no driver of its own and needs no administrator. It is only used if the sensor library did not give that disk's temperature. SATA disks still come from the library (SMART through ATA commands, with administrator).
  - **Processor**: the real frequency (base x Windows **performance %**, the same as Task Manager) and the **performance limit %** imposed by the firmware, from the `Processor Information` counters. The overlay no longer depends on the CPU tab being on to show the speed, and it keeps showing what Windows gives even if the sensor library does not load.
  - **Throttling hint without temperature**: if under high load Windows reports a performance limit or the frequency drops by more than 15%, the CPU tab warns (**CPU: hint that it is being throttled**) and the Session report counts it, making clear that it is a hint and not a temperature.
  - **CPU power without a driver**: if Windows exposes the `Energy Meter` counter (the processor's RAPL, for example on Ryzen 9000), the CPU tab, the overlay and the Session report show the package watts ("Power (Windows, RAPL)"), and "Show all" shows those of each core. If the PC has no such counter, it still says "requires driver".
  - **Hard drives (HDD) and SATA without a life %**: under "Lifetime" they show a status computed from real SMART attributes (WMI `MSStorageDriver_ATAPISmartData` and `MSStorageDriver_FailurePredictStatus`, with administrator): **Healthy (HDD)**, **Warning** (some reallocated or pending sector, spin-up retries or CRC errors) or **Bad** (Windows predicts failure, uncorrectable sectors, 100+ reallocated or 10+ pending). No percentage is made up. Below the table the status is explained and each disk's counters are listed. "Written" comes from attribute 241 if the HDD reports it (otherwise "—", and the legend says why); the SMART temperature (194/190) is used if the library did not give it.
  - **Battery** (laptops): how much it draws or charges with, in watts (`CallNtPowerInformation`).
  - Graphics card: temperature, hot spot, memory, clocks, load, VRAM, power and fans already worked without PawnIO (the library uses the DLLs of the NVIDIA or AMD driver that is already installed).
  - **Integrated graphics** (Radeon Vega / Radeon Graphics of Ryzen and Athlon, Intel UHD / Iris): the GPU card shows only what the graphics chip reports (load, clock and, if they exist, power and voltage) and the **shared memory** with the RAM, instead of leaving empty boxes; it explains that it has no VRAM of its own, fan or hot spot, and the pill says "Integrated" instead of "No data". The driver notice "over a year old, worth updating" no longer appears on integrated graphics (several stopped receiving new drivers and Windows does not say whether a newer one exists) and neither does the "VRAM full" rule. The overlay adds `{gpu.vramshared}`.
- **What's new** inside the app: the **What's new** button (at the top, next to Performance overlay) opens a window with the latest version's changes in Pulso's language (Spanish or English), without internet: if they are very long (over 2000 characters) it is cut at a bullet boundary, and a button takes you to the rest and the full history on GitHub. The English history is in `CHANGELOG.en.md`; both files are embedded in the `.exe`.

### Changes
- The **PC** tab is split into four tabs of their own: **CPU** (with motherboard, BIOS, power plan, battery and fans at the bottom), **GPU**, **Memory** and **Disks**. Each one has its own Enabled checkbox, its counter and its log; the earlier history is kept.
- The "FPS overlay" button is now called **Performance overlay**.
- **Overlay: What categories to show**: checkboxes to turn each part on or off without touching the template (FPS and 1% / 0.1% low, Frametime and graph, CPU, GPU, VRAM, RAM, Temperatures, CPU and GPU names, Time and game). It is remembered between uses in `overlay.txt`. Turning Temperatures off removes only the °C from each line.
- The tabs wrap to a second line if the window is narrow.
- **CPU temperature without drivers**: if Windows gives a credible thermal zone (normal on laptops), it is shown as "approximate, no driver". PawnIO remains an optional improvement.
- **PawnIO is no longer installed from Pulso**: the button becomes **How to install PawnIO** (or **How to update PawnIO**) and only explains, opens [pawnio.eu](https://pawnio.eu/) or copies `winget install --id namazso.PawnIO -e`, warning that the antivirus may complain and why. When you install it, Pulso detects it by itself within 2 minutes at most.
- Pulso has its **own color: green**. The icon changes (border and drawing; the background stays dark gray) and so does the window: selected tab, buttons, checkboxes and selection. The charts and the status colors (green, yellow, red) of the PC tab do not change.
- If the updated app finds another copy of the same `.exe` still open (another Windows session, or a hung older version), it closes it so the file can be replaced.
- **Performance overlay**: it now starts **without a background** (background opacity at 0). If you already had it configured, yours is kept.
- **Safer update**: before replacing the `.exe`, it checks that the download is complete (same SHA-256 that GitHub publishes) and that it really is the announced version. If the published `.exe` claimed a lower version, it is no longer installed, because otherwise the app would offer the same update forever.
- If the newly downloaded version **fails to open**, it goes back to the previous one by itself instead of leaving the app broken. The previous version is only deleted once the new one has finished starting.
- Leftovers from an update cut in half (`.nuevo`, `.roto`) are cleaned up.
- **Temperatures**: 80 °C is no longer "hot". The CPU and graphics card temperature turns yellow from 85 °C (no warning) and red, with a warning, from 90 °C. On processors with a lower limit (Ryzen X3D: 89 °C), red starts 3 °C before that limit. The overlay colors move to 85/90 °C (if you had an untouched factory model, it updates by itself).
- **Windows 7 SP1 and 8.1**: Pulso runs in a reduced form. Network, USB, Errors and Performance work (without graphics card or CPU speed data). The hardware tabs (CPU, GPU, Memory and Disks), the performance overlay and session recording are for Windows 10 and 11 only, and the app says so instead of failing.
- If **.NET Framework 4.7.2 or newer** is missing (Windows 7 SP1 and 8.1 do not ship with it), it warns and offers to open the 4.8 download, instead of closing with a confusing error. On Windows 8 (not 8.1), which cannot install it, it warns that you need to update to 8.1.

### Fixes
- **Without PawnIO, fake CPU numbers were shown**: power at 0 W, frequencies at 0 MHz and a fixed voltage of 1.55 V (the library creates those sensors even if it cannot read them). Now the CPU tab says **"CPU temperature: requires driver (PawnIO, optional)"** and "requires driver" for voltage (and for power if Windows does not give it), "Show all" lists only the load, the overlay leaves `--` (and marks the approximate thermal-zone temperature with `~`), and the Session report says "temperature / power: requires driver" instead of "- / -" and no longer says "very stable temperatures" if the CPU one was not measured.
- **The antivirus triggered when pressing "Install PawnIO"**: Pulso downloaded the driver installer from GitHub and ran it. It no longer downloads or runs any installer.
- **Defender deleted Pulso** (`Behavior:Win32/Persistence.A!ml` and `Execution.A!ml`): Pulso recreated the "Start with Windows" task with `schtasks.exe` and a temporary XML on every launch. Now it uses the Task Scheduler API and only writes it when you turn it on or when the `.exe` moved to another folder.
- **"Requires driver (PawnIO, optional)" even though PawnIO was installed**: if the sensor library opened before the driver was ready, it read zeros and the CPU tab was left without temperature, watts or voltage (showing Windows' power as if it were the only data). Now, if PawnIO 2.0 or newer is installed and Pulso has administrator rights but the processor still gives no data, it **reopens the sensors by itself** (at 10, 20 and 40 seconds and then every 2 minutes) and, meanwhile, tells the truth: "PawnIO is installed but gave no CPU data (retrying)" with what to try (close HWiNFO or FanControl, restart the PC). If Pulso is not running as administrator, it says so ("requires administrator") instead of blaming PawnIO. The log now adds, once per opening, whether there is administrator access, the PawnIO version, how long opening took, how many sensors the processor gave and the temperature and watts values.
- **CPU voltage empty on Ryzen 7000 and 9000**: those processors have no real voltage sensor, only each core's VID. The CPU tab now shows the highest VID as **Voltage (VID)**, making clear it is what the processor requests and not what is measured on the board; if a real voltage exists it is still shown as "Voltage".

## v1.0.3

### New
- **PC** tab: **real** temperature, load, speed and power of the processor and the graphics card (core, hot spot and memory), RAM load and speed, disk temperature and lifetime, fans, motherboard, BIOS, power plan and battery. All in green, yellow or red depending on how stressed it is.
- Inside "PC" there are 5 categories: **General**, **CPU**, **GPU**, **RAM** and **Disks**. Each one has its own log, its counter and its "Save warnings" checkbox. General gathers everything.
- A chart of the last 5 minutes, the session maximum and the maximum while gaming, load per core and the full list of sensors.
- **Specific warnings**, with what to try and how the PC was at that moment:
  - Processor at its temperature limit and **throttling** (it says from how many GHz to how many it dropped), or very hot for a long time (with which program was using it).
  - Hot graphics card, **hot spot far from the core** (worn thermal paste), hot video memory, stopped fans, **full VRAM** and **overloaded 12VHPWR connector**.
  - **Full RAM** (with which programs fill it), **XMP/EXPO off** (compares with the speed of your modules) and **single channel**.
  - Disks that throttle from temperature or have little lifetime left.
  - Intel 13th/14th gen with an old BIOS (the voltage degradation problem).
  - Gaming with the power plan on saver or the laptop on battery.
- **Errors** now says how the PC was right before the black screen, the blue screen, the blackout or the closed game, and whether it was heat or not (for example: "the card was at 70 °C: it was not heat, look at the driver or the power supply").
- **Performance**: when the processor throttles, it now says whether it is temperature or power (before it could only guess).
- For the CPU temperature you need **PawnIO** (a free, signed driver, the same one LibreHardwareMonitor and FanControl use): the PC tab has a button to install it and it verifies its signature before opening it.

- **FPS overlay** ("FPS overlay" button at the top): shows on top of the game FPS, 1% low, frametime, a frametime graph, CPU and GPU load and temperature, and RAM, like MSI Afterburner with RivaTuner. You choose which processes it appears in, an F1-F12 hotkey to hide it, and the corner and position. Changes apply instantly, it has a preview and you can export and import the style.
  - **100% customizable with a text template**: each line you write is a line of the overlay. You use variables between braces (`{gpu.name}`, `{fps}`, `{low1}`, `{ft}`, `{gpu.temp}`, `{gpu.vram}`, `{cpu.clock}`, `{ram.used}`...; there is a list to insert with one click), with **color** (`<c=FF0000>`), **size** (`<s=150>`), **bold** (`<b>`), **format** (`{gpu.power:0.0}`), **alert by value** (`{gpu.temp!75,85}`: orange and red; `{fps!<60,30}` the other way round for FPS) and a **frametime graph** of any size (`{graph:260x40}`). It comes with ready-made models, including an Afterburner-style one.
  - Font, size, bold, base color, and text and background opacity **separately** (per-pixel transparency).
  - **Anti-cheat safe**: it injects nothing into the game. FPS comes from Windows events (ETW, DXGI and D3D9), like PresentMon, and the text goes in a transparent window that clicks pass through. It is visible with the game in borderless fullscreen or windowed, not in exclusive fullscreen.
  - **Capturable with OBS**: it is a separate window, so it is added as a **Window Capture** source. Unchecking "Hide it when the game is not in front" keeps it visible while the game is open, even if you are looking at OBS.
  - **No cost when you are not playing**: with no game from the list open there is no ETW session or sensor reading, only one process check per second.
  - **Text outline**: black by default and 0 to 6 px, with a color of your choice, so it reads on any background.
  - **FPS average** (`{fps.avg}`) and **0.1% low** (`{low01}`), and the default template now has colors, the CPU and GPU name and the average. Anyone with the untouched old template moves to the new one by themselves.
  - New **Comparison (videos)** model: component names, large FPS, average, 1% and 0.1% low, and GPU and CPU power draw.

### Changes
- Fixed: the overlay showed **too many FPS** (for example 5000 while playing at 1000) because the timestamps of Windows events have a different clock origin from the program. Now FPS, 1% low, frametime and the graph are measured against the last frame received. Only one swapchain is also counted, so it does not add up extra if the game or an overlay presents on several.
- **Performance**: the "Graphics card at maximum" warning was removed. GPU at 100% while gaming is normal (there is no bottleneck) and the warning looked like a problem without being one. The warnings about real drops remain: processor, RAM, disk, stutters and focus loss.
- The app now **asks for administrator permissions** on opening (the sensors need it).
- "Start with Windows" uses a scheduled task (shortcuts do not elevate by themselves). It also starts even if the laptop is on battery and does not stop after 72 hours (two traps of the tasks Windows builds by default).
- If something fails on opening, instead of closing without a word it shows the error and saves it in crash.txt, in the data folder.
- Fixed: the Snipping Tool is no longer detected as a game, which gave false "lost focus" warnings in Performance.

## v1.0.2

### New
- **Performance** now warns when **another program takes focus from the game** without you touching anything (Discord, Steam, a Windows Update notice, a permission request...): which program it was, which window it opened and how long the game stayed behind, with what to try so it does not happen again. If that happened right before an FPS drop, the drop warning says so too.
- It does not count what you do yourself (Alt+Tab, the Windows key, the start menu or a click on another window).

## v1.0.1

### New
- **Performance** tab: while you play in fullscreen, it tells you what made the FPS drop. It can be a background program, the processor throttled by temperature, full RAM or the disk at 100%. It also warns when the graphics card is the limit.
- **Errors** tab: black screen from the video driver, blue screen with the code explained, sudden blackouts, hardware, disk and memory errors, and games or programs that close by themselves (with where they failed).
- **New version notice**: a yellow banner and an option in the tray. It updates itself from GitHub.
- Text **zoom**: Ctrl + mouse wheel, or Ctrl + / Ctrl - (Ctrl + 0 resets it).
- The version appears in the window title.

## v1.0.0

First version: **Network** tabs (internet drops and lag) and **USB** (what got disconnected), in the tray, with light/dark theme and Spanish/English. Logs and history in the Documents folder.

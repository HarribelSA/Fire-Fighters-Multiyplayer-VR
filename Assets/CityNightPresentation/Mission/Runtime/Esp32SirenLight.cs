using UnityEngine;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
#endif

namespace CityNightPresentation.Mission
{
    // Real emergency light: mirrors the truck's (networked) siren state to an ESP32 paired over Bluetooth
    // (Serial Port Profile -> a Windows COM port). The ESP32 sketch reads one line per command ("ON"/"OFF").
    // With this truck's relay wiring "OFF" lights the lamp and "ON" turns it off (tested by the user 2026-11-18).
    // The lamp is on only while the siren handle is pulled during the drive; it is forced off once the crew
    // arrives at the fire scene. Runs only on the Windows PC (where the ESP32 is paired); compiled out on Quest.
    // The port can be overridden from the command line:  FireFighters.exe -esp32Port=COM7
    public sealed class Esp32SirenLight : MonoBehaviour
    {
        public FireTruckMission mission;
        [Tooltip("Outgoing Bluetooth COM port of the paired ESP32 (Windows: Settings > Bluetooth > More Bluetooth settings > COM Ports).")]
        public string portName = "COM5";
        public int baudRate = 115200;
        [Tooltip("Sent when the siren handle activates the emergency (lamp on). This truck's relay lights on \"OFF\".")]
        public string sirenOnCommand = "OFF";
        [Tooltip("Sent when the emergency ends, the fire scene starts or the game closes (lamp off).")]
        public string sirenOffCommand = "ON";
        [Tooltip("Resend the current state this often, so a reconnecting ESP32 catches up.")]
        [Min(1)] public float refreshSeconds = 5;

        public string Status { get; private set; } = "idle";

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        readonly ConcurrentQueue<string> outbox = new ConcurrentQueue<string>();
        readonly AutoResetEvent signal = new AutoResetEvent(false);
        Thread worker;
        volatile bool running;
        int lastState = -1;
        float nextRefresh;

        // The COM port can only be open once: a newer instance (e.g. the freshly loaded mission truck) takes over.
        static Esp32SirenLight owner;
        string loggedStatus = "";
        void OnEnable()
        {
            foreach (var arg in Environment.GetCommandLineArgs())
                if (arg.StartsWith("-esp32Port=", StringComparison.OrdinalIgnoreCase)) portName = arg.Substring(11).Trim();
            if (owner && owner != this) owner.StopWorker(false);
            owner = this;
            running = true; lastState = -1;
            worker = new Thread(Run) { IsBackground = true, Name = "ESP32 siren light" }; worker.Start();
            Debug.Log("[ESP32] Lamp link started on " + portName + " (lamp on = \"" + sirenOnCommand + "\", off = \"" + sirenOffCommand + "\")", this);
        }
        void Update()
        {
            if (Status != loggedStatus) { loggedStatus = Status; Debug.Log("[ESP32] " + loggedStatus, this); }
            if (owner != this || !running) return;
            var fire = Fireground.FiregroundOperation.Instance;
            bool atFireScene = mission && mission.HasArrived || fire && fire.Current.phase != Fireground.FiregroundPhase.Inactive;
            int state = mission && mission.SirenActive && !atFireScene ? 1 : 0;
            if (state != lastState) Debug.Log("[ESP32] Siren " + (state == 1 ? "ON -> lamp on" : "OFF -> lamp off") + (atFireScene ? " (fire scene)" : ""), this);
            if (state != lastState || Time.unscaledTime >= nextRefresh) { lastState = state; nextRefresh = Time.unscaledTime + refreshSeconds; Send(state == 1 ? sirenOnCommand : sirenOffCommand); }
        }
        void Send(string command) { if (string.IsNullOrEmpty(command)) return; while (outbox.Count > 4 && outbox.TryDequeue(out _)) { } outbox.Enqueue(command); signal.Set(); }
        void StopWorker(bool lampOff)
        {
            if (!running && worker == null) return;
            // Always leave the real light off.
            if (lampOff) Send(sirenOffCommand);
            running = false; signal.Set();
            if (worker != null && !worker.Join(lampOff ? 4500 : 1500)) Debug.LogWarning("[ESP32] port close timed out", this);
            worker = null;
        }
        void OnDisable()
        {
            StopWorker(owner == this);
            if (owner == this) owner = null;
        }
        void OnApplicationQuit() { if (running) OnDisable(); }

        void Run()
        {
            IntPtr handle = INVALID; DateTime retry = DateTime.MinValue;
            try
            {
                while (running || !outbox.IsEmpty)
                {
                    signal.WaitOne(500);
                    if (!outbox.TryPeek(out var command)) continue;
                    if (handle == INVALID)
                    {
                        if (DateTime.UtcNow < retry) { if (!running) break; continue; }
                        handle = Open(portName, baudRate);
                        if (handle == INVALID) { Status = "cannot open " + portName + " (error " + Marshal.GetLastWin32Error() + ")"; retry = DateTime.UtcNow.AddSeconds(5); if (!running) break; continue; }
                        Status = "connected " + portName;
                        Thread.Sleep(400); // let the Bluetooth link settle so the first command is not lost
                    }
                    var bytes = System.Text.Encoding.ASCII.GetBytes(command + "\n");
                    if (WriteFile(handle, bytes, (uint)bytes.Length, out uint written, IntPtr.Zero) && written == bytes.Length) { outbox.TryDequeue(out _); Status = "sent " + command + " on " + portName; }
                    else { Status = "write failed on " + portName + " (error " + Marshal.GetLastWin32Error() + ")"; CloseHandle(handle); handle = INVALID; retry = DateTime.UtcNow.AddSeconds(2); if (!running) break; }
                }
            }
            catch (Exception e) { Status = "error " + e.Message; }
            finally { if (handle != INVALID) CloseHandle(handle); }
        }

        static IntPtr Open(string port, int baud)
        {
            var h = CreateFile(@"\\.\" + port, GENERIC_READ | GENERIC_WRITE, 0, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h == INVALID) return h;
            var dcb = new DCB(); dcb.DCBlength = (uint)Marshal.SizeOf(typeof(DCB));
            if (GetCommState(h, ref dcb)) { dcb.BaudRate = (uint)baud; dcb.ByteSize = 8; dcb.Parity = 0; dcb.StopBits = 0; dcb.Flags = 0x11; /* fBinary + DTR enable */ SetCommState(h, ref dcb); }
            var timeouts = new COMMTIMEOUTS { WriteTotalTimeoutConstant = 2000, ReadIntervalTimeout = uint.MaxValue };
            SetCommTimeouts(h, ref timeouts);
            return h;
        }

        static readonly IntPtr INVALID = new IntPtr(-1);
        const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, OPEN_EXISTING = 3;
        [StructLayout(LayoutKind.Sequential)] struct DCB { public uint DCBlength, BaudRate, Flags; public ushort wReserved, XonLim, XoffLim; public byte ByteSize, Parity, StopBits; public sbyte XonChar, XoffChar, ErrorChar, EofChar, EvtChar; public ushort wReserved1; }
        [StructLayout(LayoutKind.Sequential)] struct COMMTIMEOUTS { public uint ReadIntervalTimeout, ReadTotalTimeoutMultiplier, ReadTotalTimeoutConstant, WriteTotalTimeoutMultiplier, WriteTotalTimeoutConstant; }
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetCommState(IntPtr h, ref DCB dcb);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetCommState(IntPtr h, ref DCB dcb);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetCommTimeouts(IntPtr h, ref COMMTIMEOUTS t);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool WriteFile(IntPtr h, byte[] data, uint count, out uint written, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
#endif
    }
}

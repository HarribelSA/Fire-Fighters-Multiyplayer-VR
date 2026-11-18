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
    // (Serial Port Profile -> a Windows COM port). The ESP32 sketch reads one line per command:
    //   "ON"  -> relay pin LOW  -> light on      "OFF" -> relay pin HIGH -> light off
    // Runs only on the Windows PC (the Driver's PCVR machine where the ESP32 is paired); compiled out on Quest.
    // The port can be overridden from the command line:  FireFighters.exe -esp32Port=COM7
    public sealed class Esp32SirenLight : MonoBehaviour
    {
        public FireTruckMission mission;
        [Tooltip("Outgoing Bluetooth COM port of the paired ESP32 (Windows: Settings > Bluetooth > More Bluetooth settings > COM Ports).")]
        public string portName = "COM5";
        public int baudRate = 115200;
        [Tooltip("Sent when the siren handle activates the emergency (light on).")]
        public string sirenOnCommand = "ON";
        [Tooltip("Sent when the emergency ends, the mission ends or the game closes (light off).")]
        public string sirenOffCommand = "OFF";
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

        void OnEnable()
        {
            foreach (var arg in Environment.GetCommandLineArgs())
                if (arg.StartsWith("-esp32Port=", StringComparison.OrdinalIgnoreCase)) portName = arg.Substring(11).Trim();
            running = true; lastState = -1;
            worker = new Thread(Run) { IsBackground = true, Name = "ESP32 siren light" }; worker.Start();
        }
        void Update()
        {
            int state = mission && mission.SirenActive ? 1 : 0;
            if (state != lastState || Time.unscaledTime >= nextRefresh) { lastState = state; nextRefresh = Time.unscaledTime + refreshSeconds; Send(state == 1 ? sirenOnCommand : sirenOffCommand); }
        }
        void Send(string command) { if (string.IsNullOrEmpty(command)) return; while (outbox.Count > 4 && outbox.TryDequeue(out _)) { } outbox.Enqueue(command); signal.Set(); }
        void OnDisable()
        {
            // Always leave the real light off.
            Send(sirenOffCommand); running = false; signal.Set();
            if (worker != null && !worker.Join(1500)) Debug.LogWarning("ESP32 siren light: port close timed out", this);
            worker = null;
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

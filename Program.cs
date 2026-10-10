using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using cs2_turnbinds;
using Microsoft.Win32.SafeHandles;

public class Config
{
    public List<int> Yaw { get; set; }
    public double Sensitivity { get; set; }
    public double M_Yaw { get; set; }
    public Keys Left { get; set; }
    public Keys Right { get; set; }
    public Keys Toggle { get; set; }
    public Keys Pause { get; set; }
    public Keys Inc { get; set; }
    public Keys Dec { get; set; }
    public Keys Exit { get; set; }
    public bool PauseWhenCursorVisible { get; set; } = true;
}

[JsonSerializable(typeof(Config))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, Converters = new[] {typeof(KeysConverter)})]
public partial class ConfigJsonContext : JsonSerializerContext
{
}

public class KeysConverter : JsonConverter<Keys>
{
    public override Keys Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string keyName = reader.GetString();
        // try parse with _ prefix
        if (Enum.TryParse(typeof(Keys), "_" + keyName, out var result2))
        {
            return (Keys) result2;
        }
        
        // try parse
        if (Enum.TryParse(typeof(Keys), keyName, out var result))
        {
            return (Keys) result;
        }
       
        
        throw new JsonException($"Invalid key name: {keyName}");
    }

    public override void Write(Utf8JsonWriter writer, Keys value, JsonSerializerOptions options)
    {
        var str = value.ToString();
        // replace _ prefix with nothing
        if (str.StartsWith("_"))
        {
            writer.WriteStringValue(str.Substring(1));
        }
        else
        {
            writer.WriteStringValue(str);
        }
    }
}

class Program
{
    [StructLayout(LayoutKind.Sequential)]
    struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CursorInfo
    {
        public uint Size;
        public uint Flags;
        public IntPtr Cursor;
        public CursorPoint Position;
    }

    [DllImport("user32.dll")]
    static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);
    
    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool GetCursorInfo(ref CursorInfo cursorInfo);
    
    [DllImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerEx(IntPtr timerAttributes, string? timerName, uint flags, uint desiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period, IntPtr completionRoutine, IntPtr argument, bool resume);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
    
    [DllImport("kernel32.dll")]
    private static extern bool QueryPerformanceCounter(out long lpPerformanceCount);

    [DllImport("kernel32.dll")]
    private static extern bool QueryPerformanceFrequency(out long lpFrequency);
    
    // [DllImport("ntdll.dll")]
    // private static extern int NtQueryTimerResolution(out uint MinimumResolution, out uint MaximumResolution, out uint CurrentResolution);
    
    
    const int MOUSEEVENTF_MOVE = 0x0001;
    const int MOUSEEVENTF_ABSOLUTE = 0x8000;
    const int SM_CXSCREEN = 0;
    const int SM_CYSCREEN = 1;
    const uint CURSOR_SHOWING = 0x00000001;

    static Process? foregroundProcess;
    static uint foregroundProcessId;

    static bool IsCs2Foreground()
    {
        var window = GetForegroundWindow();
        GetWindowThreadProcessId(window, out var processId);

        try
        {
            if (processId != foregroundProcessId || foregroundProcess == null)
            {
                foregroundProcess?.Dispose();
                foregroundProcess = null;
                foregroundProcessId = 0;

                if (window == IntPtr.Zero || processId == 0)
                {
                    return false;
                }

                foregroundProcess = Process.GetProcessById(checked((int)processId));
                foregroundProcessId = processId;
            }

            return !foregroundProcess.HasExited &&
                string.Equals(foregroundProcess.ProcessName, "cs2", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception or OverflowException)
        {
            foregroundProcess?.Dispose();
            foregroundProcess = null;
            foregroundProcessId = 0;
            return false;
        }
    }
    
    static bool IsCursorHidden()
    {
        var cursorInfo = new CursorInfo { Size = (uint)Marshal.SizeOf<CursorInfo>() };
        return GetCursorInfo(ref cursorInfo) && (cursorInfo.Flags & CURSOR_SHOWING) == 0;
    }

    static bool IsGameplayInputAllowed(Config config)
    {
        return IsCs2Foreground() && (!config.PauseWhenCursorVisible || IsCursorHidden());
    }

    static void MoveMouse(int deltaX, int deltaY, Config config)
    {
        if ((deltaX != 0 || deltaY != 0) && IsGameplayInputAllowed(config))
        {
            mouse_event(MOUSEEVENTF_MOVE, deltaX, deltaY, 0, (int)IntPtr.Zero);
        }
    }

    sealed class MovementTimer : IDisposable
    {
        private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
        private const uint TIMER_MODIFY_STATE = 0x00000002;
        private const uint SYNCHRONIZE = 0x00100000;
        private readonly SafeWaitHandle timer;
        private readonly long interval = Math.Max(1, Stopwatch.Frequency / 1000);
        private long nextTick;

        public MovementTimer()
        {
            timer = CreateWaitableTimerEx(IntPtr.Zero, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_MODIFY_STATE | SYNCHRONIZE);
            if (timer.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                timer.Dispose();
                throw new Win32Exception(error, "Unable to create a high-resolution movement timer. Windows 10 version 1803 or newer is required.");
            }

            nextTick = Stopwatch.GetTimestamp() + interval;
        }

        public void WaitNextTick()
        {
            var now = Stopwatch.GetTimestamp();
            var remaining = nextTick - now;
            if (remaining > 0)
            {
                long dueTime = -Math.Max(1, remaining * 10_000_000 / Stopwatch.Frequency);
                if (!SetWaitableTimer(timer, ref dueTime, 0, IntPtr.Zero, IntPtr.Zero, false) ||
                    WaitForSingleObject(timer, uint.MaxValue) != 0)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to wait for the movement timer.");
                }
            }

            now = Stopwatch.GetTimestamp();
            nextTick += interval;
            if (nextTick <= now)
            {
                nextTick = now + interval;
            }
        }

        public void Dispose()
        {
            timer.Dispose();
        }
    }

    sealed class HotkeyCooldown
    {
        private long nextActivation;

        public bool TryActivate(long timestamp)
        {
            if (timestamp < nextActivation)
            {
                return false;
            }

            nextActivation = timestamp + Stopwatch.Frequency / 5;
            return true;
        }
    }
    
    public class MouseMoveCalculator
    {
        private long lastTime;
        private double remainingMovement;
        private long frequency;

        public MouseMoveCalculator()
        {
            QueryPerformanceCounter(out lastTime);
            QueryPerformanceFrequency(out frequency);
        }

        public void Reset()
        {
            QueryPerformanceCounter(out lastTime);
            remainingMovement = 0;
        }

        public int CalculateMovement(double yawspeed, double sensitivity, double m_yaw)
        {
            long currentTime;
            QueryPerformanceCounter(out currentTime);
            
            long elapsedTime = currentTime - lastTime;
            

            // Calculate movement basedp
            double movement = ((yawspeed / (sensitivity * m_yaw)) * elapsedTime) / frequency;

            // Accumulate remaining movement fractions to ensure precision
            remainingMovement += movement;
            long movementAmount = (long)remainingMovement;

            // Subtract the integer part used, keep the fractional part for next calculation
            remainingMovement -= movementAmount;

            // Update last time for next call
            lastTime = currentTime;

            return (int) movementAmount;
        }
    }
    
    static bool IsKeyDown(int vk)
    {
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }


    static void Main(string[] args)
    {
        
        var configFilePath = "config.json";
        var configFileExists = File.Exists(configFilePath);
        

        string configFile;
        if (!configFileExists)
        {
            var defaultConfig = new Config
            {
                Yaw = new List<int> { 80, 160, 240 },
                Sensitivity = 0.8,
                M_Yaw = 0.022,
                Left = Keys.LBUTTON,
                Right = Keys.RBUTTON,
                Toggle = Keys.XBUTTON1,
                Pause = Keys.F1,
                Inc = Keys.OEM_PLUS,
                Dec = Keys.OEM_MINUS,
                Exit = Keys.F2
            };
            configFile = JsonSerializer.Serialize(defaultConfig, typeof(Config), ConfigJsonContext.Default);
            File.WriteAllText(configFilePath, configFile);
        }
        else
        {
            configFile = File.ReadAllText(configFilePath);
        }
        
        // use system.json
        var config = JsonSerializer.Deserialize(configFile, typeof(Config), ConfigJsonContext.Default) as Config;
        if (config == null)
        {
            throw new Exception("Config file is empty");
        }

        Console.WriteLine(@"
   ____   _____ ______    _____ _    _ _____  ______ 
  / __ \ / ____|  ____|  / ____| |  | |  __ \|  ____|
 | |  | | |    | |__    | (___ | |  | | |__) | |__   
 | |  | | |    |  __|    \___ \| |  | |  _  /|  __|  
 | |__| | |____| |____ _ ____) | |__| | | \ \| |     
  \____/ \_____|______(_)_____/ \____/|_|  \_\_|     ");
        
        Console.WriteLine();
        
        
        Console.WriteLine("Yaw speeds: " + string.Join(", ", config.Yaw));
        Console.WriteLine("Sensitivity: " + config.Sensitivity);
        Console.WriteLine("M_Yaw: " + config.M_Yaw);
        Console.WriteLine("Left: " + config.Left);
        Console.WriteLine("Right: " + config.Right);
        Console.WriteLine("Toggle Yaw: " + config.Toggle);
        Console.WriteLine("Pause: " + config.Pause);
        Console.WriteLine("Increase Yaw: " + config.Inc);
        Console.WriteLine("Decrease Yaw: " + config.Dec);
        Console.WriteLine("Exit: " + config.Exit);
        Console.WriteLine("Pause when cursor visible: " + config.PauseWhenCursorVisible);
        
        Console.WriteLine();
        Console.WriteLine("To edit bindings, edit config.json and restart the program.");
        Console.WriteLine("Use the keynames from here without the VK_ : https://learn.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes");
        Console.WriteLine("IE: VK_LBUTTON is LBUTTON etc, A-Z are the same, 0-9 are the same, F1-F24 are the same, etc.");
        
        
        Console.WriteLine();
        Console.WriteLine("Current yaw speed: " + config.Yaw[0]);
        Console.WriteLine("PAUSED, press " + config.Pause + " to unpause");
        Console.WriteLine("Movement and gameplay hotkeys only work while CS2 is focused. Exit works globally.");
        
        int currentYawIndex = 0;
        bool paused = true;
        var calculator = new MouseMoveCalculator();
        using var movementTimer = new MovementTimer();
        var pauseCooldown = new HotkeyCooldown();
        var increaseCooldown = new HotkeyCooldown();
        var decreaseCooldown = new HotkeyCooldown();
        var toggleCooldown = new HotkeyCooldown();

        while (true)
        {
            movementTimer.WaitNextTick();

            if (IsKeyDown((int) config.Exit))
            {
                Console.WriteLine("Exiting program...");
                break;
            }

            if (!IsCs2Foreground())
            {
                calculator.Reset();
                continue;
            }

            if (IsGameplayInputAllowed(config))
            {
                var movement = calculator.CalculateMovement(config.Yaw[currentYawIndex],  config.Sensitivity, config.M_Yaw);

                if (IsKeyDown((int) config.Left) && !paused)
                {
                    MoveMouse(-movement, 0, config);
                }

                if (IsKeyDown((int) config.Right) && !paused)
                {
                    MoveMouse(movement, 0, config);
                }
            }
            else
            {
                calculator.Reset();
            }

            var timestamp = Stopwatch.GetTimestamp();

            // Check if the 'P' key is pressed to toggle pause state for left mouse button
            if (IsCs2Foreground() && IsKeyDown((int) config.Pause) && pauseCooldown.TryActivate(timestamp))
            {
                paused = !paused;
                if (paused)
                {
                    Console.WriteLine("PAUSED press " + config.Pause + " to unpause");
                }
                else
                {
                    Console.WriteLine("ACTIVE press " + config.Pause + " to pause");
                }
            }

            // Check if the '+' key is pressed to increase yaw speed
            if (IsGameplayInputAllowed(config) && IsKeyDown((int) config.Inc) && !paused && increaseCooldown.TryActivate(timestamp))
            {
                config.Yaw[currentYawIndex] += 10; // Increase yaw speed by 10
                Console.WriteLine($"Yaw speed increased to {config.Yaw[currentYawIndex]}");
            }

            // Check if the '-' key is pressed to decrease yaw speed
            if (IsGameplayInputAllowed(config) && IsKeyDown((int) config.Dec) && !paused && decreaseCooldown.TryActivate(timestamp))
            {
                config.Yaw[currentYawIndex] -= 10; // Decrease yaw speed by 10
                Console.WriteLine($"Yaw speed decreased to {config.Yaw[currentYawIndex]}");
            }
            
            if (IsGameplayInputAllowed(config) && IsKeyDown((int) config.Toggle) && !paused && toggleCooldown.TryActivate(timestamp))
            {
                currentYawIndex = (currentYawIndex + 1) % config.Yaw.Count;
                Console.WriteLine($"Yaw speed toggled to {config.Yaw[currentYawIndex]}");
            }
        }
    }
}

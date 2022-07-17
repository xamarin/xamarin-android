using System.ComponentModel;
using System.Runtime.InteropServices;

const uint CTRL_C_EVENT = 0;

if (args.Length != 1) {
	Console.Error.WriteLine ("Usage: console-ctrl [pid]");
	return -1;
}
if (!int.TryParse (args [0], out int pid)) {
	Console.Error.WriteLine ($"Unable to parse pid: {args [0]}");
	return -1;
}

if (!FreeConsole ())
	ThrowWin32Error (nameof (FreeConsole));
if (!AttachConsole ((uint) pid))
	ThrowWin32Error (nameof (AttachConsole));
if (!SetConsoleCtrlHandler (null, add: true))
	ThrowWin32Error (nameof (SetConsoleCtrlHandler));
if (!GenerateConsoleCtrlEvent (CTRL_C_EVENT, 0))
	ThrowWin32Error (nameof (GenerateConsoleCtrlEvent));
if (!FreeConsole ())
	ThrowWin32Error (nameof (FreeConsole));
if (!SetConsoleCtrlHandler (null, add: false))
	ThrowWin32Error (nameof (SetConsoleCtrlHandler));

Console.WriteLine ($"Stopped successfully: {pid}");
return 0;

static void ThrowWin32Error (string name)
{
	// Win32Exception which has the textual error message for Marshal.GetLastPInvokeError()
	var exc = new Win32Exception();
	Console.Error.WriteLine ($"{name} failed with:");
	Console.Error.WriteLine (exc.ToString ()); 
	Environment.Exit (exc.ErrorCode);
}

[DllImport ("kernel32.dll", SetLastError = true)]
[return: MarshalAs (UnmanagedType.Bool)]
static extern bool GenerateConsoleCtrlEvent (uint dwCtrlEvent, uint dwProcessGroupId);

[DllImport ("kernel32.dll", SetLastError = true)]
static extern bool AttachConsole (uint dwProcessId);

[DllImport ("kernel32.dll", SetLastError = true)]
static extern bool FreeConsole ();

[DllImport ("kernel32.dll", SetLastError = true)]
static extern bool SetConsoleCtrlHandler (ConsoleCtrlDelegate? handler, bool add);

delegate bool ConsoleCtrlDelegate (uint type);
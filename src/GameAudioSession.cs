using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

// Read-only Core Audio diagnostics, restricted to this game's PID or exact executable path.
internal static class GameAudioSession
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDevice(string device, System.Text.StringBuilder target, int size);
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class DeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out IDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionManager
    {
        [PreserveSig] int GetAudioSessionControl(ref Guid session, uint flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(ref Guid session, uint flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out ISessionEnumerator sessions);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object control);
    }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionControl
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(ref Guid grouping, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionVolume
    {
        [PreserveSig] int SetMasterVolume(float volume, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float volume);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
    [ComImport, Guid("1C158861-B533-4B30-B1CF-E853E51C59B8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IChannelVolume
    {
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetChannelVolume(uint index, float level, ref Guid context);
        [PreserveSig] int GetChannelVolume(uint index, out float level);
    }

    internal static int Inspect(int pid, string executable, Action<string> log)
    {
        object enumerator = null;
        IDeviceCollection devices = null;
        int matches = 0;
        try
        {
            enumerator = new DeviceEnumerator();
            Marshal.ThrowExceptionForHR(((IDeviceEnumerator)enumerator).EnumAudioEndpoints(0, 1, out devices));
            uint count; Marshal.ThrowExceptionForHR(devices.GetCount(out count));
            for (uint i = 0; i < count; i++)
            {
                IDevice device = null;
                try { Marshal.ThrowExceptionForHR(devices.Item(i, out device)); matches += InspectDevice(device, pid, executable, log); }
                catch (COMException ex) { log("音频输出 " + i + " 检查失败：" + ex.Message); }
                finally { Release(device); }
            }
            return matches;
        }
        finally { Release(devices); Release(enumerator); }
    }
    private static int InspectDevice(IDevice device, int pid, string executable, Action<string> log)
    {
        object activated = null; ISessionEnumerator sessions = null;
        int matches = 0;
        try
        {
            Guid iid = typeof(ISessionManager).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out activated));
            Marshal.ThrowExceptionForHR(((ISessionManager)activated).GetSessionEnumerator(out sessions));
            int count; Marshal.ThrowExceptionForHR(sessions.GetCount(out count));
            string fullPath = Path.GetFullPath(executable);
            string kernelPath = null;
            if (fullPath.Length > 2 && fullPath[1] == ':')
            {
                System.Text.StringBuilder target = new System.Text.StringBuilder(1024);
                if (QueryDosDevice(fullPath.Substring(0, 2), target, target.Capacity) != 0)
                    kernelPath = target.ToString() + fullPath.Substring(2);
            }
            for (int i = 0; i < count; i++)
            {
                object raw = null;
                try
                {
                    Marshal.ThrowExceptionForHR(sessions.GetSession(i, out raw));
                    ISessionControl control = (ISessionControl)raw;
                    uint processId; string id;
                    Marshal.ThrowExceptionForHR(control.GetProcessId(out processId));
                    Marshal.ThrowExceptionForHR(control.GetSessionIdentifier(out id));
                    bool match = pid > 0 ? processId == (uint)pid :
                        id != null && (id.IndexOf(fullPath + "%b", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        (kernelPath != null && id.IndexOf(kernelPath + "%b", StringComparison.OrdinalIgnoreCase) >= 0));
                    if (!match || control.IsSystemSoundsSession() == 0) continue;
                    matches++;
                    ISessionVolume volume = (ISessionVolume)raw;
                    float level; bool muted;
                    Marshal.ThrowExceptionForHR(volume.GetMasterVolume(out level));
                    Marshal.ThrowExceptionForHR(volume.GetMute(out muted));
                    log("游戏 Windows 音频会话：pid=" + processId + "，音量=" + (level * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%，静音=" + muted);
                    IChannelVolume channels = (IChannelVolume)raw;
                    uint channelCount; Marshal.ThrowExceptionForHR(channels.GetChannelCount(out channelCount));
                    for (uint channel = 0; channel < channelCount; channel++)
                    {
                        float gain; Marshal.ThrowExceptionForHR(channels.GetChannelVolume(channel, out gain));
                        log("会话声道 " + channel + " 音量=" + (gain * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%");
                    }

                }
                finally { Release(raw); }
            }
        }
        finally { Release(sessions); Release(activated); }
        return matches;
    }
    private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
}

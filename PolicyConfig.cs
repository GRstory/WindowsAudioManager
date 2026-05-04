using System;
using System.Runtime.InteropServices;

namespace WindowsAudioManager;

internal enum ERole : uint
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2
}

[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int GetMixFormat();
    [PreserveSig] int GetDeviceFormat();
    [PreserveSig] int ResetDeviceFormat();
    [PreserveSig] int SetDeviceFormat();
    [PreserveSig] int GetProcessingPeriod();
    [PreserveSig] int SetProcessingPeriod();
    [PreserveSig] int GetShareMode();
    [PreserveSig] int SetShareMode();
    [PreserveSig] int GetPropertyValue();
    [PreserveSig] int SetPropertyValue();
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, ERole eRole);
    [PreserveSig] int SetEndpointVisibility();
}

[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal class CPolicyConfigClient { }

internal static class PolicyConfig
{
    public static void SetDefaultDevice(string deviceId)
    {
        object? clientObj = null;
        try
        {
            clientObj = new CPolicyConfigClient();
            var client = (IPolicyConfig)clientObj;

            int hr = client.SetDefaultEndpoint(deviceId, ERole.eConsole);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);

            hr = client.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);

            hr = client.SetDefaultEndpoint(deviceId, ERole.eCommunications);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        }
        finally
        {
            if (clientObj != null) Marshal.ReleaseComObject(clientObj);
        }
    }
}

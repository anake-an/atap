using System.Runtime.InteropServices;
using Atap.Interop;

namespace Atap.Platform.Power;

/// <summary>Puts Atap in Windows' efficiency mode and takes it out again. Tests pass their own.</summary>
internal interface IProcessEfficiency
{
    /// <summary>True asks Windows to run Atap slower and cooler (EcoQoS). False lets Windows decide again.</summary>
    /// <returns>False when Windows refused, for example on versions before Windows 11.</returns>
    bool Set(bool efficient);
}

/// <summary>EcoQoS on the whole process through <c>SetProcessInformation</c>.</summary>
internal sealed class ProcessEfficiency : IProcessEfficiency
{
    public bool Set(bool efficient)
    {
        var state = new NativeMethods.PROCESS_POWER_THROTTLING_STATE
        {
            Version = NativeMethods.PROCESS_POWER_THROTTLING_CURRENT_VERSION,
            // Off clears the mask, which hands the choice back to Windows rather than forcing full speed.
            ControlMask = efficient ? NativeMethods.PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0,
            StateMask = efficient ? NativeMethods.PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0,
        };
        return NativeMethods.SetProcessInformation(
            NativeMethods.GetCurrentProcess(),
            NativeMethods.ProcessPowerThrottling,
            ref state,
            Marshal.SizeOf<NativeMethods.PROCESS_POWER_THROTTLING_STATE>());
    }
}

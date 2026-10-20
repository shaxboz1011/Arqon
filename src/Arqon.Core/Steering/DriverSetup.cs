namespace Arqon.Core.Steering;

/// <summary>What the one-click setup of system mode did.</summary>
/// <param name="ExclusionAdded">Defender was told to leave the driver folder alone.</param>
public sealed record DriverSetupResult(bool ExclusionAdded);

/// <summary>
/// The steps that make system mode work on a computer that has never had the driver, in the order that survives an antivirus:
/// first the driver folder is excluded from Defender, then the genuine driver is downloaded and installed into it, then both files
/// are checked again (still there, validly signed). Each step is injected, so the order and the failures can be tested.
/// </summary>
public sealed class DriverSetup(
    string folder,
    Func<string, bool> excludeFromDefender,
    Func<IProgress<double>?, CancellationToken, Task> install,
    Func<string, bool> signatureIsValid)
{
    public async Task<DriverSetupResult> RunAsync(IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        Directory.CreateDirectory(folder);
        var excluded = excludeFromDefender(folder);

        await install(progress, cancel).ConfigureAwait(false);

        var dll = Path.Combine(folder, WinDivertLocator.DllName);
        var sys = Path.Combine(folder, WinDivertLocator.SysName);
        if (!File.Exists(dll) || !File.Exists(sys))
            throw new DriverSetupException(excluded
                ? "The driver files disappeared right after they were installed. Another antivirus program probably removed them: allow the folder " + folder + " in it."
                : "The driver files disappeared right after they were installed. An antivirus program removed them: allow the folder " + folder + " in it (for Windows Defender this needs administrator rights).");

        if (!signatureIsValid(sys))   // the driver is signed by its author; the DLL is not
        {
            TryDelete(dll);
            TryDelete(sys);
            throw new DriverSetupException("Windows does not accept the signature of the driver, so it was removed again.");
        }
        return new DriverSetupResult(excluded);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

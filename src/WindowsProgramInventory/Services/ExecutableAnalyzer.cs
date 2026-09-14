using System.Diagnostics;
using System.IO;
using WindowsProgramInventory.Core.Interfaces;
using WindowsProgramInventory.Models;

namespace WindowsProgramInventory.Services;

/// <summary>
/// Reads metadata from an executable: file version info, timestamps and PE architecture.
/// </summary>
public sealed class ExecutableAnalyzer : IExecutableAnalyzer
{
    private readonly ILogger _logger;

    public ExecutableAnalyzer(ILogger? logger = null)
    {
        _logger = logger ?? new SilentLogger();
    }

    public void Analyze(ProgramInfo program)
    {
        var path = program.ExecutablePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            var fileInfo = new FileInfo(path);

            var vi = FileVersionInfo.GetVersionInfo(path);

            program.FileSizeBytes = fileInfo.Exists ? fileInfo.Length : null;
            program.CreatedDate = fileInfo.Exists ? fileInfo.CreationTime : null;
            program.ModifiedDate = fileInfo.Exists ? fileInfo.LastWriteTime : null;
            program.FileVersion = NullIfEmpty(vi.FileVersion);
            program.ProductVersion = NullIfEmpty(vi.ProductVersion);
            program.ProductName = NullIfEmpty(vi.ProductName);
            program.CompanyName = NullIfEmpty(vi.CompanyName);
            program.Copyright = NullIfEmpty(vi.LegalCopyright);
            program.OriginalFilename = NullIfEmpty(vi.OriginalFilename);
            program.Description = NullIfEmpty(vi.FileDescription);
            program.Architecture = PeArchitecture.Detect(path);

            if (program.Type == ProgramType.Unknown || program.Type == ProgramType.Shortcut)
            {
                program.Type = ProgramType.DesktopApp;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Executable analysis failed: {path} ({ex.Message})");
        }
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>Reads the PE header Machine field to detect x86/x64/ARM64 without any dependency.</summary>
internal static class PeArchitecture
{
    private const ushort MachineI386 = 0x014c;
    private const ushort MachineAmd64 = 0x8664;
    private const ushort MachineArm64 = 0xaa64;

    public static ProgramArchitecture Detect(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new BinaryReader(stream);

            if (stream.Length < 64 || reader.ReadUInt16() != 0x5a4d /* MZ */)
            {
                return ProgramArchitecture.Unknown;
            }

            stream.Seek(0x3c, SeekOrigin.Begin);
            var peOffset = reader.ReadInt32();
            if (peOffset < 0 || peOffset + 6 > stream.Length)
            {
                return ProgramArchitecture.Unknown;
            }

            stream.Seek(peOffset, SeekOrigin.Begin);
            if (reader.ReadUInt32() != 0x00004550 /* PE\0\0 */)
            {
                return ProgramArchitecture.Unknown;
            }

            var machine = reader.ReadUInt16();
            return machine switch
            {
                MachineI386 => ProgramArchitecture.X86,
                MachineAmd64 => ProgramArchitecture.X64,
                MachineArm64 => ProgramArchitecture.Arm64,
                _ => ProgramArchitecture.Unknown,
            };
        }
        catch
        {
            return ProgramArchitecture.Unknown;
        }
    }
}
namespace WindowsProgramInventory.Models;

/// <summary>
/// Executable architecture, read from the PE header only when available. Never guessed.
/// </summary>
public enum ProgramArchitecture
{
    Unknown = 0,
    X86 = 1,
    X64 = 2,
    Arm64 = 3,
}
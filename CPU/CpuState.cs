namespace ProjetoSimIO.Cpu
{
    /// <summary>
    /// Representa o estado interno da CPU: registradores, flags e status.
    /// </summary>
    public class CpuState
    {
        public uint ProgramCounter { get; set; }
        public uint Accumulator { get; set; }
        public bool InterruptEnabled { get; set; } = true;
        public bool Halted { get; set; } = false;
        public uint LastAccessAddress { get; set; }
        public uint LastReadData { get; set; }
        public uint LastWriteData { get; set; }
        public string CurrentOperation { get; set; } = "NOP";

        public void Reset()
        {
            ProgramCounter = 0;
            Accumulator = 0;
            InterruptEnabled = true;
            Halted = false;
            LastAccessAddress = 0;
            LastReadData = 0;
            LastWriteData = 0;
            CurrentOperation = "RESET";
        }
    }
}

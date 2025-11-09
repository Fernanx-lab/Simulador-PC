using ProjetoSimIO.Core;

namespace ProjetoSimIO.Cpu
{
    /// <summary>
    /// Responsável por simular a execução de instruções.
    /// Pode ser expandido para um conjunto de instruções real.
    /// </summary>
    public class InstructionExecutor
    {
        private readonly IBus bus;
        private readonly CpuState state;
        private readonly Metrics metrics;

        public InstructionExecutor(IBus bus, CpuState state, Metrics metrics)
        {
            this.bus = bus;
            this.state = state;
            this.metrics = metrics;
        }

        public void ExecuteNextInstruction()
        {
            // Exemplo básico: LOAD e STORE simulados
            state.CurrentOperation = "LOAD";
            uint addr = state.ProgramCounter;
            uint data = bus.Read32(addr);
            state.LastAccessAddress = addr;
            state.LastReadData = data;
            state.Accumulator = data;

            state.CurrentOperation = "STORE";
            bus.Write32(0x10000100, state.Accumulator);
            state.LastWriteData = state.Accumulator;

            state.ProgramCounter += 4;
            metrics.InstructionsExecuted++;
        }
    }
}

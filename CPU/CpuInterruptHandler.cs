using ProjetoSimIO.Core;

namespace ProjetoSimIO.Cpu
{
    /// <summary>
    /// Responsável por gerenciar interrupções e executar ISRs simuladas.
    /// </summary>
    public class CpuInterruptHandler
    {
        private readonly IPicController pic;
        private readonly CpuState state;
        private readonly Metrics metrics;

        public CpuInterruptHandler(IPicController pic, CpuState state, Metrics metrics)
        {
            this.pic = pic;
            this.state = state;
            this.metrics = metrics;
        }

        public void HandleInterrupt(int vector)
        {
            state.CurrentOperation = $"INT_{vector}";
            state.InterruptEnabled = false;

            // ISR simulada
            metrics.TimerInterruptCount++;

            // Ao final da ISR
            state.InterruptEnabled = true;
        }
    }
}

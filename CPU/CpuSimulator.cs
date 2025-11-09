using ProjetoSimIO.Core;

namespace ProjetoSimIO.Cpu
{
    /// <summary>
    /// Classe principal da CPU simulada.
    /// Controla execução, interrupções e integração com o barramento.
    /// </summary>
    public class CpuSimulator : ICpu
    {
        public string Name => "CPU0";
        public int Priority => 0;
        public bool WantsBus => pendingAccess;

        private readonly IBus bus;
        private readonly IPicController pic;
        private readonly Metrics metrics;
        private readonly CpuState state;
        private readonly InstructionExecutor executor;
        private readonly CpuInterruptHandler irqHandler;

        private bool pendingAccess = false;
        private ulong cycleCount = 0;

        public CpuSimulator(IBus bus, IPicController pic, Metrics metrics)
        {
            this.bus = bus;
            this.pic = pic;
            this.metrics = metrics;
            this.state = new CpuState();
            this.executor = new InstructionExecutor(bus, state, metrics);
            this.irqHandler = new CpuInterruptHandler(pic, state, metrics);
        }

        public void StepInstruction() => executor.ExecuteNextInstruction();
        public bool IrqPending() => pic.HasPendingIrq();
        public void AckIrq(int vector) => pic.AckIrq(vector);

        public void Tick()
        {
            cycleCount++;

            if (IrqPending() && state.InterruptEnabled)
            {
                int vector = pic.GetPendingVector();
                irqHandler.HandleInterrupt(vector);
                AckIrq(vector);
            }

            if (!state.Halted)
                StepInstruction();

            metrics.TotalCycles = (long)cycleCount;
        }

        public void OnBusGranted() => pendingAccess = false;
    }
}

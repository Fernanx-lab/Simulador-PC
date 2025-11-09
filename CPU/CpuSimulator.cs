using ProjetoSimIO.Core;

namespace ProjetoSimIO.Cpu
{
    public class CpuSimulator : ICpu
    {
        public string Name => "CPU0";
        public int Priority => 0; // prioridade para arbitragem do barramento
        public bool WantsBus => pendingAccess;

        private readonly IBus bus;
        private readonly Metrics metrics;
        private readonly IPicController pic;

        private bool pendingAccess = false;
        private ulong cycleCount = 0;

        public CpuSimulator(IBus bus, IPicController pic, Metrics metrics)
        {
            this.bus = bus;
            this.pic = pic;
            this.metrics = metrics;
        }

        // Executa uma “instrução simulada”
        public void StepInstruction()
        {
            // Exemplo: leitura de um dado em memória
            uint address = 0x00001000;
            uint data = bus.Read32(address);

            // Escrita em dispositivo MMIO
            bus.Write32(0x10000100, data);

            metrics.InstructionsExecuted++;
        }

        // Consulta se há IRQ pendente
        public bool IrqPending()
        {
            return pic.HasPendingIrq();
        }

        // Reconhece uma interrupção
        public void AckIrq(int vector)
        {
            pic.AckIrq(vector);
        }

        // Um “tick” do simulador
        public void Tick()
        {
            cycleCount++;

            if (IrqPending())
            {
                // Medir latência e tratar IRQ
                int vector = pic.GetPendingVector();
                HandleInterrupt(vector);
                AckIrq(vector);
            }

            // Simular execução normal
            StepInstruction();

            metrics.TotalCycles = (long)cycleCount;
        }

        private void HandleInterrupt(int vector)
        {
            // ISR simulada
            metrics.TimerInterruptCount++;
        }

        public void OnBusGranted()
        {
            pendingAccess = false;
        }
    }
}

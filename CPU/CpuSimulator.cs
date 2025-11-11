using System;
using ProjetoSimuladorPC.RAM;
using ProjetoSimIO.Core;
using ProjetoSimuladorPC.Cpu;
using ProjetoSimuladorPC.Utilidades;

namespace ProjetoSimuladorPC.Cpu
{
    /// <summary>
    /// Simulador de CPU que acessa RamState diretamente (sem IBus).
    /// Agora usa CpuInterruptHandler para tratar IRQs e não implementa mais ICpu.
    /// </summary>
    public class CpuSimulator
    {
        public string Name => "CPU0";
        public int Priority => 0;
        public bool WantsBus => false; // sem barramento

        private readonly RamState ram;
        private readonly IPicController controladorPic;
        private readonly Metrics metricas;
        private readonly CpuState estado;
        private readonly InstructionExecutor executor;
        private readonly CpuInterruptHandler tratadorIrq;

        private ulong contadorCiclos = 0;

        public CpuSimulator(RamState ram, IPicController controladorPic, Metrics metricas)
        {
            this.ram = ram ?? throw new ArgumentNullException(nameof(ram));
            this.controladorPic = controladorPic ?? throw new ArgumentNullException(nameof(controladorPic));
            this.metricas = metricas ?? throw new ArgumentNullException(nameof(metricas));
            estado = new CpuState();
            executor = new InstructionExecutor(ram, estado, this.metricas);
            tratadorIrq = new CpuInterruptHandler(controladorPic, estado, this.metricas);

            this.ram.MemoryChanged += Ram_MemoryChanged;
        }

        private void Ram_MemoryChanged(object? sender, MemoryChangedEventArgs e)
        {
            // Reação a mudanças na RAM: atualizar métricas ou invalidar caches locais.
            try { metricas.MemoryWrites++; } catch { }
        }

        public void StepInstruction() => executor.ExecuteNextInstruction();
        public bool IrqPending() => controladorPic.HasPendingIrq();
        public void AckIrq(int vector) => controladorPic.AckIrq(vector);

        public void Tick()
        {
            contadorCiclos++;

            if (IrqPending() && estado.InterrupcaoHabilitada)
            {
                int vetor = controladorPic.GetPendingVector();
                if (vetor >= 0)
                {
                    // Delega o tratamento ao handler desacoplado.
                    tratadorIrq.HandleInterrupt(vetor);
                    // Nota: tratador realiza o ACK ao PIC.
                }
            }

            if (!estado.Parado)
                StepInstruction();

            try { metricas.TotalCycles = (long)contadorCiclos; }
            catch { }
        }

        // Exponha retorno do ISR se outro código precisar invocá-lo
        public void ReturnFromInterrupt() => tratadorIrq.ReturnFromInterrupt();
    }
}
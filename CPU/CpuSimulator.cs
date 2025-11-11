CPU\CpuSimulator.cs
using System;
using ProjetoSimuladorPC.RAM;
using ProjetoSimIO.Core;

namespace ProjetoSimuladorPC.CPU
{
    /// <summary>
    /// Simulador de CPU que acessa RamState diretamente (sem IBus).
    /// Inscreve-se em MemoryChanged para reagir a alterações se necessário.
    /// </summary>
    public class CpuSimulator : ICpu
    {
        public string Name => "CPU0";
        public int Priority => 0;
        public bool WantsBus => false; // sem barramento

        private readonly RamState ram;
        private readonly IPicController controladorPic;
        private readonly dynamic metricas;
        private readonly CpuState estado;
        private readonly InstructionExecutor executor;

        private ulong contadorCiclos = 0;

        public CpuSimulator(RamState ram, IPicController controladorPic, dynamic metricas)
        {
            this.ram = ram ?? throw new ArgumentNullException(nameof(ram));
            this.controladorPic = controladorPic ?? throw new ArgumentNullException(nameof(controladorPic));
            this.metricas = metricas;
            estado = new CpuState();
            executor = new InstructionExecutor(ram, estado, metricas);

            this.ram.MemoryChanged += Ram_MemoryChanged;
        }

        private void Ram_MemoryChanged(object? sender, MemoryChangedEventArgs e)
        {
            // Exemplo de reação a mudanças na RAM: atualizar métricas ou invalidar caches locais.
            if (metricas != null)
            {
                try { metricas.MemoryWrites++; }
                catch { }
            }
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
                HandleInterrupt(vetor);
                AckIrq(vetor);
            }

            if (!estado.Parado)
                StepInstruction();

            try { metricas.TotalCycles = (long)contadorCiclos; }
            catch { }
        }

        private void HandleInterrupt(int vetor)
        {
            // Tratamento simples: atualiza PC para vetor * 4 (exemplo)
            estado.ContadorPrograma = vetor * 4;
            try { metricas.InterruptsHandled++; } catch { }
        }
    }
}
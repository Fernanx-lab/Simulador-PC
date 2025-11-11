CPU\InstructionExecutor.cs
using System;
using ProjetoSimuladorPC.RAM;

namespace ProjetoSimuladorPC.Cpu
{
    /// <summary>
    /// Executor de instruções que opera diretamente sobre RamState (sem barramento).
    /// Implementação simples: LOAD/STORE de 4 bytes (uint).
    /// </summary>
    public class InstructionExecutor
    {
        private readonly RamState ram;
        private readonly CpuState estado;
        private readonly dynamic metricas;

        public InstructionExecutor(RamState ram, CpuState estado, dynamic metricas)
        {
            this.ram = ram ?? throw new ArgumentNullException(nameof(ram));
            this.estado = estado ?? throw new ArgumentNullException(nameof(estado));
            this.metricas = metricas;
        }

        public void ExecuteNextInstruction()
        {
            // Exemplo: carregar 4 bytes do endereço do PC e armazenar em um MMIO exemplo.
            estado.OperacaoAtual = "LOAD";
            int endereco = estado.ContadorPrograma;

            byte[] dadosLidos;
            try
            {
                dadosLidos = ram.Ler(endereco, 4);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Em caso de acesso inválido, marque operação e retorne
                estado.OperacaoAtual = "FAULT";
                return;
            }

            uint valor = BitConverter.ToUInt32(dadosLidos, 0);
            estado.UltimoEnderecoAcesso = endereco;
            estado.UltimoDadoLido = valor;
            estado.Acumulador = valor;

            // STORE de exemplo: escreve acumulador em endereço fixo 0x100 (exemplo)
            estado.OperacaoAtual = "STORE";
            byte[] bytesEscrita = BitConverter.GetBytes(estado.Acumulador);
            try
            {
                ram.Escrever(0x100, bytesEscrita); // usa int
                estado.UltimoDadoEscrito = estado.Acumulador;
            }
            catch (ArgumentOutOfRangeException)
            {
                estado.OperacaoAtual = "FAULT";
                return;
            }

            estado.ContadorPrograma += 4;

            if (metricas != null)
            {
                try { metricas.InstructionsExecuted++; }
                catch { }
            }
        }
    }
}
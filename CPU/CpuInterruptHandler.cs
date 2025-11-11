using System;
using ProjetoSimuladorPC.RAM;
using ProjetoSimIO.Core;
using ProjetoSimuladorPC.Cpu;

namespace ProjetoSimuladorPC.Cpu
{
    /// <summary>
    /// Tratador desacoplado de interrupções da CPU.
    /// Implementação mínima: atualiza PC, desabilita interrupções enquanto no ISR,
    /// atualiza métricas e envia ACK ao PIC.
    /// </summary>
    public class CpuInterruptHandler
    {
        private readonly IPicController _pic;
        private readonly CpuState _estado;
        private readonly dynamic _metricas;
        private int _nivelNesting = 0;

        public CpuInterruptHandler(IPicController pic, CpuState estado, dynamic metricas)
        {
            _pic = pic ?? throw new ArgumentNullException(nameof(pic));
            _estado = estado ?? throw new ArgumentNullException(nameof(estado));
            _metricas = metricas;
        }

        public void HandleInterrupt(int vetor)
        {
            // Marca entrada no ISR
            _nivelNesting++;
            _estado.Parado = false;
            _estado.InterrupcaoHabilitada = false;

            // Convenção simples: endereço do ISR = vetor * 4
            _estado.ContadorPrograma = vetor * 4;

            try { _metricas.InterruptsHandled++; } catch { }

            // Acknowledge no PIC (ignora erros)
            try { _pic.AckIrq(vetor); } catch { }
        }

        public void ReturnFromInterrupt()
        {
            if (_nivelNesting > 0) _nivelNesting--;
            if (_nivelNesting == 0)
            {
                _estado.InterrupcaoHabilitada = true;
            }

            try { _metricas.InterruptsReturned++; } catch { }
        }
    }
}

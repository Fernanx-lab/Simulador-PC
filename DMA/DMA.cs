using System.Threading.Tasks;
using ProjetoSimuladorPC.RAM;

namespace ProjetoSimuladorPC.DMA
{
    /// <summary>
    /// DMA que opera diretamente sobre a fachada concreta RamState e preenche a fachada DmaState.
    /// Não faz I/O (Console) e não usa interfaces de memória que força injeção de dependência.
    /// </summary>
    public class DMA
    {
        private readonly RamState _ram;
        private readonly DispositivoMMIO _dispositivo;
        private readonly DmaState _state;

        private readonly object _sync = new();

        public DMA(RamState ram, DispositivoMMIO dispositivo, DmaState state)
        {
            _ram = ram;
            _dispositivo = dispositivo;
            _state = state;
        }

        /// <summary>
        /// Inicia a transferência DMA de forma assíncrona. Retorna uma Task que completa quando a transferência termina.
        /// </summary>
        public async Task ExecutarTransferenciaAsync(int origem, int destino, int tamanho, int delayMs = 10)
        {
            lock (_sync)
            {
                if (_state.EmExecucao)
                {
                    _state.Fail("Já existe uma transferência em andamento");
                    return;
                }
                _state.Start(origem, destino, tamanho);
            }

            int transferidos = 0;
            try
            {
                for (int i = 0; i < tamanho; i++)
                {
                    // Ler 1 byte da RAM (pode lançar se fora dos limites)
                    byte dado = _ram.Ler(origem + i);

                    // Se destino for MMIO -> enviar ao dispositivo, caso contrário escrever na RAM
                    if (_dispositivo.EstaNaFaixa(destino + i))
                    {
                        _dispositivo.ReceberDado(dado);
                    }
                    else
                    {
                        _ram.Escrever(destino + i, dado);
                    }

                    transferidos = i + 1;
                    // Atualiza progresso ocasionalmente (cada byte aqui; UI decide frequência)
                    _state.ReportProgress(transferidos);

                    // Simula tempo de hardware de forma assíncrona
                    await Task.Delay(delayMs).ConfigureAwait(false);
                }

                _state.Complete();
            }
            catch (System.Exception ex)
            {
                _state.Fail($"Falha na transferência: {ex.Message}");
            }
        }

        /// <summary>
        /// Fornece snapshot do estado DMA (conveniente para a UI).
        /// </summary>
        public DmaSnapshot GetStateSnapshot() => _state.GetSnapshot();
    }
}

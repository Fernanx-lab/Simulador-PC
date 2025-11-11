using System;

namespace ProjetoSimuladorPC.DMA
{
    /// <summary>
    /// Estado do DMA usado como fachada para a UI (Blazor).
    /// A instância é preenchida pela classe DMA sem imprimir em console.
    /// </summary>
    public class DmaState
    {
        private readonly object _sync = new();

        public bool EmExecucao { get; private set; }
        public bool TransferenciaConcluida { get; private set; }

        // Informações da transferência atual
        public int Origem { get; private set; }
        public int Destino { get; private set; }
        public int Tamanho { get; private set; }
        public int BytesTransferidos { get; private set; }

        // Timestamps e mensagens de status para a UI
        public DateTime? Inicio { get; private set; }
        public DateTime? Fim { get; private set; }
        public string? Mensagem { get; private set; }

        // Evento para notificar a UI (Blazor) sobre mudanças no estado
        public event EventHandler? StateChanged;

        internal void Start(int origem, int destino, int tamanho)
        {
            lock (_sync)
            {
                EmExecucao = true;
                TransferenciaConcluida = false;
                Origem = origem;
                Destino = destino;
                Tamanho = tamanho;
                BytesTransferidos = 0;
                Inicio = DateTime.UtcNow;
                Fim = null;
                Mensagem = "Em execução";
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void ReportProgress(int bytes)
        {
            lock (_sync)
            {
                BytesTransferidos = bytes;
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void SetMessage(string mensagem)
        {
            lock (_sync)
            {
                Mensagem = mensagem;
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void Complete()
        {
            lock (_sync)
            {
                EmExecucao = false;
                TransferenciaConcluida = true;
                Fim = DateTime.UtcNow;
                Mensagem = "Concluída";
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void Fail(string mensagem)
        {
            lock (_sync)
            {
                EmExecucao = false;
                TransferenciaConcluida = false;
                Fim = DateTime.UtcNow;
                Mensagem = mensagem;
            }
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Retorna um snapshot simples (imutável) do estado atual para renderização.
        /// </summary>
        public DmaSnapshot GetSnapshot()
        {
            lock (_sync)
            {
                return new DmaSnapshot(
                    EmExecucao,
                    TransferenciaConcluida,
                    Origem,
                    Destino,
                    Tamanho,
                    BytesTransferidos,
                    Inicio,
                    Fim,
                    Mensagem ?? string.Empty
                );
            }
        }
    }

    public record DmaSnapshot(
        bool EmExecucao,
        bool TransferenciaConcluida,
        int Origem,
        int Destino,
        int Tamanho,
        int BytesTransferidos,
        DateTime? Inicio,
        DateTime? Fim,
        string Mensagem
    );
}

using System;

namespace ProjetoSimuladorPC.DMA
{
    /// <summary>
    /// Dispositivo MMIO simples que armazena bytes em um buffer interno.
    /// Não realiza I/O; fornece acesso ao buffer para inspeção via UI.
    /// </summary>
    public class DispositivoMMIO
    {
        private readonly int inicioFaixa;
        private readonly int fimFaixa;
        private readonly byte[] buffer;
        private int pos = 0;

        public DispositivoMMIO(int inicio, int fim)
        {
            inicioFaixa = inicio;
            fimFaixa = fim;
            buffer = new byte[(fim - inicio) + 1];
        }

        /// <summary>
        /// Retorna true se o endereço está dentro da faixa do MMIO.
        /// </summary>
        public bool EstaNaFaixa(int endereco)
        {
            return endereco >= inicioFaixa && endereco <= fimFaixa;
        }

        /// <summary>
        /// Recebe e armazena um dado no registrador interno do MMIO.
        /// Não escreve em console — a UI pode inspecionar o buffer via GetBufferSnapshot().
        /// </summary>
        public void ReceberDado(byte valor)
        {
            if (pos >= buffer.Length) pos = 0;
            buffer[pos] = valor;
            pos++;
        }

        /// <summary>
        /// Retorna uma cópia do buffer interno para inspeção (seguro para a UI).
        /// </summary>
        public byte[] GetBufferSnapshot()
        {
            var copia = new byte[buffer.Length];
            System.Array.Copy(buffer, copia, buffer.Length);
            return copia;
        }

        /// <summary>
        /// Faixa inicial (inclusive) do MMIO.
        /// </summary>
        public int InicioFaixa => inicioFaixa;

        /// <summary>
        /// Faixa final (inclusive) do MMIO.
        /// </summary>
        public int FimFaixa => fimFaixa;
    }
}
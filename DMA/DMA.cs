using System;
using System.Threading.Tasks;

namespace ProjetoSimuladorPC.DMA
{
    // ===================================================
    // INTERFACE DA RAM (deve permanecer porque outro dev fará a RAM real)
    // ===================================================
    public interface IMemoriaRAM
    {
        byte Ler(int endereco);
        void Escrever(int endereco, byte valor);
    }

    // ===================================================
    // IMPLEMENTAÇÃO DA MMIO
    // ===================================================
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
        /// </summary>
        public void ReceberDado(byte valor)
        {
            Console.WriteLine($"[MMIO] Recebeu valor: {valor}");

            // Evita overflow do buffer
            if (pos >= buffer.Length)
                pos = 0;

            buffer[pos] = valor;
            Console.WriteLine($"[MMIO] Registrador {pos} armazenou valor: {valor}");

            pos++;
        }
    }

    // ===================================================
    // CLASSE DMA (usando RAM + MMIO)
    // ===================================================
    public class DMA
    {
        private readonly IMemoriaRAM memoria;
        private readonly DispositivoMMIO dispositivo;

        public bool EmExecucao { get; private set; }
        public bool TransferenciaConcluida { get; private set; }

        /// <summary>
        /// Construtor: recebe a RAM (interface) e o MMIO
        /// </summary>
        public DMA(IMemoriaRAM mem, DispositivoMMIO disp)
        {
            memoria = mem;
            dispositivo = disp;
            EmExecucao = false;
            TransferenciaConcluida = false;
        }

        /// <summary>
        /// Realiza a transferência direta entre RAM e MMIO.
        /// </summary>
        public async void ExecutarTransferencia(int origem, int destino, int tamanho)
        {
            if (EmExecucao)
            {
                Console.WriteLine("[DMA] Já existe uma transferência em andamento!");
                return;
            }

            EmExecucao = true;
            TransferenciaConcluida = false;

            Console.WriteLine($"[DMA] Iniciando transferência de {tamanho} bytes de {origem} -> {destino}");

            await Task.Run(() =>
            {
                for (int i = 0; i < tamanho; i++)
                {
                    byte dado = memoria.Ler(origem + i);

                    if (dispositivo.EstaNaFaixa(destino + i))
                    {
                        dispositivo.ReceberDado(dado);
                    }
                    else
                    {
                        memoria.Escrever(destino + i, dado);
                    }

                    Task.Delay(10).Wait(); // Simula tempo de hardware
                }
            });

            EmExecucao = false;
            TransferenciaConcluida = true;

            Console.WriteLine("[DMA] Transferência finalizada.");
        }
    }
}

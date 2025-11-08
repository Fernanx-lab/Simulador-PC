using System;
using System.Threading.Tasks;

//
// ===============================
//  MÓDULO: DMA (Direct Memory Access)
//  Desenvolvido por: Paula Silvério de Castro
//  Função: Gerenciar transferências diretas entre RAM e MMIO
// ===============================
//

/// <summary>
/// Interface da memória RAM.
/// Essa parte será implementada por outro desenvolvedor.
/// A DMA apenas usa esses métodos para ler e escrever dados.
/// </summary>
public interface IMemoriaRAM
{
    byte Ler(int endereco);
    void Escrever(int endereco, byte valor);
}

/// <summary>
/// Interface de um dispositivo MMIO (Memory-Mapped I/O).
/// Também será implementada por outro desenvolvedor.
/// </summary>
public interface IDispositivoMMIO
{
    bool EstaNaFaixa(int endereco);
    void ReceberDado(byte valor);
}

/// <summary>
/// Classe que representa a DMA (Direct Memory Access).
/// 
/// Ela é o "assistente" da CPU: recebe um pedido para transferir dados,
/// e copia diretamente da RAM para outro local da RAM ou para um dispositivo MMIO.
/// 
/// Observação:
///  - A RAM e a CPU são implementadas por outros desenvolvedores.
///  - A DMA apenas usa os métodos públicos dessas classes (Ler / Escrever / etc).
/// </summary>
public class DMA
{
    // Referências para os outros módulos do sistema
    private IMemoriaRAM memoria;          // Interface da memória (RAM)
    private IDispositivoMMIO dispositivo; // Interface do dispositivo MMIO

    // Estados internos da DMA
    public bool EmExecucao { get; private set; }
    public bool TransferenciaConcluida { get; private set; }

    /// <summary>
    /// Construtor: a CPU ou o sistema principal injeta as dependências da RAM e do dispositivo.
    /// </summary>
    public DMA(IMemoriaRAM mem, IDispositivoMMIO disp)
    {
        memoria = mem;
        dispositivo = disp;
        EmExecucao = false;
        TransferenciaConcluida = false;
    }

    /// <summary>
    /// Método principal da DMA.
    /// 
    /// A CPU vai chamar este método, informando:
    ///   - Endereço de origem (de onde os dados vêm)
    ///   - Endereço de destino (para onde os dados vão)
    ///   - Tamanho (quantos bytes copiar)
    /// 
    /// Se o destino estiver dentro da área MMIO, os dados são enviados ao dispositivo.
    /// Caso contrário, são gravados normalmente na memória.
    /// </summary>
    public async void ExecutarTransferencia(int origem, int destino, int tamanho)
    {
        // Impede duas transferências simultâneas
        if (EmExecucao)
        {
            Console.WriteLine("[DMA] Já estou executando uma transferência!");
            return;
        }

        EmExecucao = true;
        TransferenciaConcluida = false;

        Console.WriteLine($"[DMA] Iniciando transferência: {tamanho} bytes de {origem} -> {destino}");

        // A DMA trabalha de forma autônoma (em paralelo à CPU)
        await Task.Run(() =>
        {
            for (int i = 0; i < tamanho; i++)
            {
                // Lê o dado da origem
                byte dado = memoria.Ler(origem + i);

                // Se o destino for MMIO, envia pro dispositivo
                if (dispositivo.EstaNaFaixa(destino + i))
                {
                    dispositivo.ReceberDado(dado);
                }
                else
                {
                    // Caso contrário, grava na memória normalmente
                    memoria.Escrever(destino + i, dado);
                }

                Task.Delay(10).Wait(); // Simula tempo de transferência
            }
        });

        EmExecucao = false;
        TransferenciaConcluida = true;
        Console.WriteLine("[DMA] Transferência concluída! CPU pode continuar o trabalho.");
    }
}

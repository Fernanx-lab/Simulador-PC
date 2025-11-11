using ProjetoSimuladorPC.Cache;
using ProjetoSimuladorPC.Cpu;
using ProjetoSimuladorPC.DMA;
using ProjetoSimuladorPC.RAM;

namespace ProjetoSimuladorPC.Utilidades
{
    /// <summary>
    /// Fachada suprema do simulador — agrega os estados dos módulos e fornece
    /// snapshots / eventos para a UI (Blazor). Não faz I/O; apenas expõe dados.
    /// </summary>
    public class SimulationState
    {
        private readonly object _sync = new();

        // Configurações fixas (YAML)
        public Configuracoes Config { get; set; } = new Configuracoes();

        // Controle do clock
        public long CicloAtual { get; set; } = 0;

        // Estados dos módulos (existem em suas pastas)
        public CpuState Cpu { get; set; } = new CpuState();
        public CacheState Cache { get; set; } = new CacheState();
        public RamState Ram { get; set; } = new RamState(1); // default 1MB — sobrescreva conforme necessário
        public DmaState Dma { get; set; } = new DmaState();

        // Evento para notificar UI sobre mudança no estado (ex.: Blazor components podem assinar)
        public event EventHandler? StateChanged;

        /// <summary>
        /// Dispara o evento StateChanged de forma thread-safe.
        /// Use quando alguma atualização importante ocorrer (tick, acesso, escrita etc.).
        /// </summary>
        public void NotifyStateChanged()
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Atualiza o ciclo atual e notifica assinantes.
        /// </summary>
        public void AdvanceCycle(long delta = 1)
        {
            lock (_sync)
            {
                CicloAtual += delta;
            }
            NotifyStateChanged();
        }

        /// <summary>
        /// Retorna um snapshot imutável e pequeno do estado do simulador pronto para renderização na UI.
        /// RamPreviewLength limita a quantidade de bytes lidos da RAM para evitar snapshots enormes.
        /// </summary>
        public SimulationSnapshot GetSnapshot(int ramPreviewAddress = 0, int ramPreviewLength = 16)
        {
            lock (_sync)
            {
                // CPU snapshot
                var cpu = new CpuSnapshot(
                    ContadorPrograma: Cpu.ContadorPrograma,
                    Acumulador: Cpu.Acumulador,
                    InterrupcaoHabilitada: Cpu.InterrupcaoHabilitada,
                    Parado: Cpu.Parado,
                    UltimoEnderecoAcesso: Cpu.UltimoEnderecoAcesso,
                    UltimoDadoLido: Cpu.UltimoDadoLido,
                    UltimoDadoEscrito: Cpu.UltimoDadoEscrito,
                    OperacaoAtual: Cpu.OperacaoAtual ?? string.Empty
                );

                // Cache snapshot (cópia simples dos campos essenciais)
                var cache = new CacheSnapshot(
                    Reads: Cache.Reads,
                    Writes: Cache.Writes,
                    Hits: Cache.Hits,
                    Misses: Cache.Misses,
                    MemoryWrites: Cache.MemoryWrites,
                    CacheSizeBytes: Cache.CacheSizeBytes,
                    BlockSizeBytes: Cache.BlockSizeBytes,
                    Associativity: Cache.Associativity,
                    NumSets: Cache.NumSets,
                    ReplacementPolicy: Cache.ReplacementPolicy ?? string.Empty,
                    WritePolicy: Cache.WritePolicy ?? string.Empty,
                    HitRate: Cache.HitRate,
                    MissRate: Cache.MissRate
                );

                // RAM preview — tenta ler, respeitando limites
                byte[] ramPreview = Array.Empty<byte>();
                bool ramPreviewOk = false;
                try
                {
                    if (ramPreviewLength > 0)
                    {
                        if (ramPreviewAddress < 0) ramPreviewAddress = 0;
                        if (ramPreviewAddress + ramPreviewLength > Ram.TamanhoEmBytes)
                        {
                            ramPreviewLength = Math.Max(0, Ram.TamanhoEmBytes - ramPreviewAddress);
                        }

                        if (ramPreviewLength > 0 && Ram.TryLer(ramPreviewAddress, ramPreviewLength, out var dados))
                        {
                            ramPreview = dados;
                            ramPreviewOk = true;
                        }
                    }
                }
                catch
                {
                    // fallback: preview stays empty
                }

                var ram = new RamSnapshot(
                    TamanhoEmBytes: Ram.TamanhoEmBytes,
                    TamanhoEmMB: Ram.TamanhoEmMB,
                    PreviewAddress: ramPreviewAddress,
                    Preview: ramPreview,
                    PreviewAvailable: ramPreviewOk
                );

                // DMA snapshot (usar o snapshot fornecido pela DmaState)
                var dmaSnapshot = Dma.GetSnapshot();

                return new SimulationSnapshot(
                    CicloAtual: CicloAtual,
                    TimestampUtc: DateTime.UtcNow,
                    Cpu: cpu,
                    Cache: cache,
                    Ram: ram,
                    Dma: dmaSnapshot,
                    Config: Config
                );
            }
        }
    }

    // --- Snapshot / DTO records usados pela UI (imutáveis e simples) ---

    public record SimulationSnapshot(
        long CicloAtual,
        DateTime TimestampUtc,
        CpuSnapshot Cpu,
        CacheSnapshot Cache,
        RamSnapshot Ram,
        DmaSnapshot Dma,
        Configuracoes Config
    );

    public record CpuSnapshot(
        int ContadorPrograma,
        uint Acumulador,
        bool InterrupcaoHabilitada,
        bool Parado,
        int UltimoEnderecoAcesso,
        uint UltimoDadoLido,
        uint UltimoDadoEscrito,
        string OperacaoAtual
    );

    public record CacheSnapshot(
        ulong Reads,
        ulong Writes,
        ulong Hits,
        ulong Misses,
        ulong MemoryWrites,
        int CacheSizeBytes,
        int BlockSizeBytes,
        int Associativity,
        int NumSets,
        string ReplacementPolicy,
        string WritePolicy,
        double HitRate,
        double MissRate
    );

    public record RamSnapshot(
        int TamanhoEmBytes,
        int TamanhoEmMB,
        int PreviewAddress,
        byte[] Preview,
        bool PreviewAvailable
    );
}

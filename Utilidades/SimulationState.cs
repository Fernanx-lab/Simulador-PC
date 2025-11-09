using System.Net.NetworkInformation;
using System.Xml;
using ram;

namespace ProjetoSimuladorPC.Utilidades
{
    public class SimulationState
    {
        // Configurações fixas (YAML)
        public Configuracoes Config { get; set; } = new Configuracoes();

        // Controle do clock
        public long CicloAtual { get; set; } = 0;

        // Estados dos módulos
        public CpuState Cpu { get; set; } = new CpuState();
        public CacheState Cache { get; set; } = new CacheState();
        public RamState Ram { get; set; } = new RamState();
        public DmaState Dma { get; set; } = new DmaState();
        public PicState Pic { get; set; } = new PicState();
        public DevicesState Devices { get; set; } = new DevicesState();

        // 🔹 Novo: estado do barramento separado
        public BarramentoState Barramento { get; set; } = new BarramentoState();

        // Métricas acumuladas
        public MetricsState Metrics { get; set; } = new MetricsState();
    }
}

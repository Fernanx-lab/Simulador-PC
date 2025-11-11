ProjetoSimuladorPC/Utilidades/Metrics.cs
namespace ProjetoSimuladorPC.Utilidades
{
    /// <summary>
    /// Métricas mínimas usadas pela CPU e handlers.
    /// </summary>
    public class Metrics
    {
        public long InstructionsExecuted { get; set; }
        public long TotalCycles { get; set; }
        public long MemoryWrites { get; set; }
        public long InterruptsHandled { get; set; }
        public long InterruptsReturned { get; set; }
    }
}
using System;

namespace ProjetoSimuladorPC.Cache
{
    /// <summary>
    /// Fachada de estado da cache. Outras partes do sistema (ex.: Blazor) consultam esta classe
    /// para obter estatísticas e metadados da cache sem depender de escrita em console.
    /// </summary>
    public class CacheState
    {
        private readonly object _sync = new();

        // Contadores observáveis
        public ulong Reads { get; private set; }
        public ulong Writes { get; private set; }
        public ulong Hits { get; private set; }
        public ulong Misses { get; private set; }
        public ulong MemoryWrites { get; private set; }

        // Metadados da configuração da cache
        public int CacheSizeBytes { get; private set; }
        public int BlockSizeBytes { get; private set; }
        public int Associativity { get; private set; }
        public int NumSets { get; private set; }
        public string ReplacementPolicy { get; private set; } = string.Empty;
        public string WritePolicy { get; private set; } = string.Empty;

        // Info derivada
        public double HitRate
        {
            get
            {
                lock (_sync)
                {
                    var total = (double)(Reads + Writes);
                    return total > 0 ? (double)Hits / total : 0.0;
                }
            }
        }

        public double MissRate
        {
            get
            {
                lock (_sync)
                {
                    var total = (double)(Reads + Writes);
                    return total > 0 ? (double)Misses / total : 0.0;
                }
            }
        }

        public DateTime LastUpdated { get; private set; } = DateTime.MinValue;

        /// <summary>
        /// Atualiza os contadores e metadados — chamado pela implementação da simulação.
        /// Método thread-safe.
        /// </summary>
        public void Update(
            ulong reads,
            ulong writes,
            ulong hits,
            ulong misses,
            ulong memoryWrites,
            int cacheSizeBytes,
            int blockSizeBytes,
            int associativity,
            int numSets,
            string replacementPolicy,
            string writePolicy)
        {
            lock (_sync)
            {
                Reads = reads;
                Writes = writes;
                Hits = hits;
                Misses = misses;
                MemoryWrites = memoryWrites;

                CacheSizeBytes = cacheSizeBytes;
                BlockSizeBytes = blockSizeBytes;
                Associativity = associativity;
                NumSets = numSets;
                ReplacementPolicy = replacementPolicy ?? string.Empty;
                WritePolicy = writePolicy ?? string.Empty;

                LastUpdated = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Atualiza apenas os contadores (uso quando metadados já foram inicializados).
        /// </summary>
        public void UpdateCounters(ulong reads, ulong writes, ulong hits, ulong misses, ulong memoryWrites)
        {
            lock (_sync)
            {
                Reads = reads;
                Writes = writes;
                Hits = hits;
                Misses = misses;
                MemoryWrites = memoryWrites;
                LastUpdated = DateTime.UtcNow;
            }
        }
    }
}

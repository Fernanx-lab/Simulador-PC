using System;

namespace ProjetoSimuladorPC.Cache
{
    // Políticas de substituição / escrita
    public enum ReplacementPolicy { LRU, FIFO }
    public enum WritePolicy { WriteBack, WriteThrough }

    // Representa uma linha/bloco de cache (estrutura interna)
    public class CacheBlock
    {
        public bool Valid { get; set; }
        public ulong Tag { get; set; }
        public bool Dirty { get; set; }
        public ulong LastUsedCounter { get; set; } // para LRU
        public ulong InsertCounter { get; set; } // para FIFO

        public CacheBlock()
        {
            Valid = false;
            Tag = 0;
            Dirty = false;
            LastUsedCounter = 0;
            InsertCounter = 0;
        }
    }

    // Representa um conjunto (set) da cache
    public class CacheSet
    {
        public CacheBlock[] Lines { get; private set; }
        public int Associativity => Lines.Length;

        public CacheSet(int assoc)
        {
            Lines = new CacheBlock[assoc];
            for (int i = 0; i < assoc; i++) Lines[i] = new CacheBlock();
        }
    }

    /// <summary>
    /// Implementação da cache que NÃO usa Console. Em vez disso preenche a fachada <see cref="CacheState"/>.
    /// Projetada para ser usada em aplicações (ex.: Blazor) onde a UI consulta a <see cref="CacheState"/>.
    /// </summary>
    public class Cache
    {
        readonly int cacheSizeBytes;
        readonly int blockSizeBytes;
        readonly int associativity;
        readonly int numSets;
        readonly ReplacementPolicy replPolicy;
        readonly WritePolicy writePolicy;

        readonly CacheSet[] sets;

        // Fachada de estado opcional (preenchida pela simulação)
        readonly CacheState? _state;

        // Contadores internos
        ulong globalCounter = 1; // para timestamps LRU/FIFO
        public ulong Reads { get; private set; }
        public ulong Writes { get; private set; }
        public ulong Hits { get; private set; }
        public ulong Misses { get; private set; }
        public ulong MemoryWrites { get; private set; } // conta escritas para memória principal

        /// <summary>
        /// Cria uma nova instância da cache. Se fornecer <paramref name="state"/>, a cache irá preenchê-la
        /// com metadados e contadores; caso contrário comportamento fica apenas interno.
        /// </summary>
        public Cache(int cacheSizeBytes, int blockSizeBytes, int associativity, ReplacementPolicy replPolicy, WritePolicy writePolicy, CacheState? state = null)
        {
            if (blockSizeBytes <= 0 || cacheSizeBytes <= 0) throw new ArgumentException("Tamanhos devem ser positivos.");
            if (cacheSizeBytes % blockSizeBytes != 0) throw new ArgumentException("CacheSize deve ser múltiplo de BlockSize.");

            this.cacheSizeBytes = cacheSizeBytes;
            this.blockSizeBytes = blockSizeBytes;
            this.associativity = associativity;
            int numLines = cacheSizeBytes / blockSizeBytes;
            if (associativity <= 0 || associativity > numLines) throw new ArgumentException("Associatividade inválida.");
            this.numSets = numLines / associativity;
            this.replPolicy = replPolicy;
            this.writePolicy = writePolicy;

            sets = new CacheSet[numSets];
            for (int i = 0; i < numSets; i++) sets[i] = new CacheSet(associativity);

            Reads = Writes = Hits = Misses = MemoryWrites = 0;

            _state = state;
            if (_state != null)
            {
                // Inicializa metadados + contadores iniciais
                _state.Update(Reads, Writes, Hits, Misses, MemoryWrites,
                    cacheSizeBytes, blockSizeBytes, associativity, numSets,
                    replPolicy.ToString(), writePolicy.ToString());
            }
        }

        // Decodifica endereço (32 bits) em tag, índice do conjunto e offset
        void DecodeAddress(uint address, out ulong tag, out int setIndex, out int offset)
        {
            int offsetBits = (int)Math.Log(blockSizeBytes, 2);
            int setBits = (int)Math.Log(numSets, 2);
            if (Math.Pow(2, offsetBits) != blockSizeBytes) offsetBits = CountBitsNeeded(blockSizeBytes); // fallback
            if (Math.Pow(2, setBits) != numSets) setBits = CountBitsNeeded(numSets);

            offset = (int)(address & ((uint)(blockSizeBytes - 1)));
            if (numSets > 1)
            {
                uint setMask = (uint)((1 << setBits) - 1);
                setIndex = (int)((address >> offsetBits) & setMask);
            }
            else setIndex = 0;

            tag = (ulong)(address >> (offsetBits + (numSets > 1 ? (int)Math.Log(numSets, 2) : 0)));
        }

        int CountBitsNeeded(int v)
        {
            int bits = 0;
            int val = v;
            while ((1 << bits) < val) bits++;
            return bits;
        }

        /// <summary>
        /// Simula um acesso à cache. Caso <paramref name="isWrite"/> seja true, é uma escrita.
        /// Todos os resultados são refletidos nos contadores e na <see cref="CacheState"/> (se fornecida).
        /// Não realiza I/O (Console).
        /// </summary>
        public void Access(uint address, bool isWrite)
        {
            if (isWrite) Writes++; else Reads++;

            DecodeAddress(address, out ulong tag, out int setIndex, out int offset);
            var set = sets[setIndex];

            // Verifica hit
            for (int i = 0; i < set.Associativity; i++)
            {
                var line = set.Lines[i];
                if (line.Valid && line.Tag == tag)
                {
                    // Hit
                    Hits++;
                    line.LastUsedCounter = globalCounter++;
                    if (isWrite)
                    {
                        if (writePolicy == WritePolicy.WriteThrough)
                        {
                            MemoryWrites++;
                        }
                        else // Write-Back
                        {
                            line.Dirty = true;
                        }
                    }

                    UpdateStateCounters();
                    return;
                }
            }

            // Miss
            Misses++;

            // tenta encontrar linha inválida
            for (int i = 0; i < set.Associativity; i++)
            {
                var line = set.Lines[i];
                if (!line.Valid)
                {
                    FillLine(line, tag, isWrite);
                    UpdateStateCounters();
                    return;
                }
            }

            // substitui de acordo com política
            int victim = SelectVictimLine(set);
            var victimLine = set.Lines[victim];
            if (victimLine.Dirty && victimLine.Valid && writePolicy == WritePolicy.WriteBack)
            {
                MemoryWrites++;
            }

            FillLine(victimLine, tag, isWrite);
            UpdateStateCounters();
        }

        void FillLine(CacheBlock line, ulong tag, bool isWrite)
        {
            line.Valid = true;
            line.Tag = tag;
            line.LastUsedCounter = globalCounter++;
            line.InsertCounter = globalCounter; // marca inserção para FIFO
            line.Dirty = isWrite && writePolicy == WritePolicy.WriteBack;
            if (isWrite && writePolicy == WritePolicy.WriteThrough)
            {
                MemoryWrites++;
            }
        }

        int SelectVictimLine(CacheSet set)
        {
            int victim = 0;
            if (replPolicy == ReplacementPolicy.LRU)
            {
                ulong min = ulong.MaxValue;
                for (int i = 0; i < set.Associativity; i++)
                {
                    if (set.Lines[i].LastUsedCounter < min)
                    {
                        min = set.Lines[i].LastUsedCounter;
                        victim = i;
                    }
                }
            }
            else // FIFO
            {
                ulong min = ulong.MaxValue;
                for (int i = 0; i < set.Associativity; i++)
                {
                    if (set.Lines[i].InsertCounter < min)
                    {
                        min = set.Lines[i].InsertCounter;
                        victim = i;
                    }
                }
            }
            return victim;
        }

        /// <summary>
        /// Atualiza os contadores dentro da fachada (se presente).
        /// Use este método para forçar sincronização com a fachada sem alterar contadores internos.
        /// </summary>
        public void UpdateState()
        {
            if (_state == null) return;
            _state.Update(Reads, Writes, Hits, Misses, MemoryWrites,
                cacheSizeBytes, blockSizeBytes, associativity, numSets,
                replPolicy.ToString(), writePolicy.ToString());
        }

        void UpdateStateCounters()
        {
            if (_state == null) return;
            _state.UpdateCounters(Reads, Writes, Hits, Misses, MemoryWrites);
        }

        /// <summary>
        /// Retorna uma cópia simples do layout atual da cache (para UI/inspeção).
        /// Cada conjunto contém um array de tuplas (valid, tag, dirty).
        /// </summary>
        public (bool valid, ulong tag, bool dirty)[][] GetSetsSnapshot()
        {
            var snapshot = new (bool, ulong, bool)[numSets][];
            for (int s = 0; s < numSets; s++)
            {
                var lines = sets[s].Lines;
                var arr = new (bool, ulong, bool)[lines.Length];
                for (int i = 0; i < lines.Length; i++)
                {
                    var l = lines[i];
                    arr[i] = (l.Valid, l.Tag, l.Dirty);
                }
                snapshot[s] = arr;
            }
            return snapshot;
        }
    }
}
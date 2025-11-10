using System;
using System.Collections.Generic;
using System.Linq;

/*
  Simulador de Memória Cache (C# Console)

  - Suporta: mapeamento por conjuntos (n-way set associative), mapeamento direto (associativity = 1), totalmente associativo (associativity = number of lines)
  - Políticas de substituição: LRU (padrão) e FIFO
  - Políticas de escrita: Write-Back (com bit dirty) e Write-Through
  - Entrada interativa: comandos "R <hex_address>" para leitura e "W <hex_address>" para escrita.
  - Exemplo de uso: R 0x1A3F  -> lê do endereço 0x1A3F

  Para compilar: dotnet new console -n CacheSim
                 substituir o conteúdo de Program.cs por este arquivo
                 dotnet run
*/

namespace CacheSimulator
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Simulador de Memória Cache - C# ===\n");

            // Configuração (você pode ajustar aqui ou implementar leitura de arquivo/config)
            int cacheSize = AskInt("Tamanho da cache (bytes)", 1024);
            int blockSize = AskInt("Tamanho do bloco/linha (bytes)", 16);
            int associativity = AskInt("Associatividade (1 = direto, >1 = n-way, 0 = totalmente associativa)", 1);
            if (associativity == 0)
                associativity = (cacheSize / blockSize); // totalmente associativa

            string repl = AskOption("Política de substituição (LRU/FIFO)", new[] { "LRU", "FIFO" }, "LRU");
            string writePolicy = AskOption("Política de escrita (WB=Write-Back / WT=Write-Through)", new[] { "WB", "WT" }, "WB");

            Cache cache = new Cache(cacheSize, blockSize, associativity, repl == "LRU" ? ReplacementPolicy.LRU : ReplacementPolicy.FIFO, writePolicy == "WB" ? WritePolicy.WriteBack : WritePolicy.WriteThrough);

            Console.WriteLine("\nComandos: \n  R <hex_addr>  -> Ler (ex: R 0x1A3F)\n  W <hex_addr>  -> Escrever\n  STATS         -> Mostrar estatísticas\n  EXIT          -> Sair\n");

            while (true)
            {
                Console.Write("> ");
                var line = Console.ReadLine();
                if (line == null) break;
                line = line.Trim();
                if (line.Length == 0) continue;

                if (string.Equals(line, "EXIT", StringComparison.OrdinalIgnoreCase)) break;
                if (string.Equals(line, "STATS", StringComparison.OrdinalIgnoreCase))
                {
                    cache.PrintStats();
                    continue;
                }

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    Console.WriteLine("Comando inválido. Use R <addr> ou W <addr>.\n");
                    continue;
                }

                var op = parts[0].ToUpper();
                if (!TryParseHex(parts[1], out uint addr))
                {
                    Console.WriteLine("Endereço inválido. Use formato hexadecimal, ex: 0x1A3F ou 1A3F\n");
                    continue;
                }

                if (op == "R") cache.Access(addr, false);
                else if (op == "W") cache.Access(addr, true);
                else Console.WriteLine("Operação inválida. Use R ou W.\n");
            }

            Console.WriteLine("Saindo...\n");
            cache.PrintStats();
        }

        static int AskInt(string prompt, int defaultVal)
        {
            Console.Write($"{prompt} [{defaultVal}]: ");
            var s = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(s)) return defaultVal;
            if (int.TryParse(s.Trim(), out int v)) return v;
            return defaultVal;
        }

        static string AskOption(string prompt, string[] options, string defaultVal)
        {
            Console.Write($"{prompt} [{defaultVal}]: ");
            var s = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(s)) return defaultVal;
            var up = s.Trim().ToUpper();
            return options.Contains(up) ? up : defaultVal;
        }

        static bool TryParseHex(string s, out uint value)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            return uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out value);
        }
    }

    enum ReplacementPolicy { LRU, FIFO }
    enum WritePolicy { WriteBack, WriteThrough }

    class CacheBlock
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

    class CacheSet
    {
        public CacheBlock[] Lines { get; private set; }
        public int Associativity => Lines.Length;

        public CacheSet(int assoc)
        {
            Lines = new CacheBlock[assoc];
            for (int i = 0; i < assoc; i++) Lines[i] = new CacheBlock();
        }
    }

    class Cache
    {
        readonly int cacheSizeBytes;
        readonly int blockSizeBytes;
        readonly int associativity;
        readonly int numSets;
        readonly ReplacementPolicy replPolicy;
        readonly WritePolicy writePolicy;

        readonly CacheSet[] sets;

        // Counters
        ulong globalCounter = 1; // para LRU/FIFO timestamps
        public ulong Reads { get; private set; }
        public ulong Writes { get; private set; }
        public ulong Hits { get; private set; }
        public ulong Misses { get; private set; }
        public ulong MemoryWrites { get; private set; } // conta writes para mem principal (write-through ou write-back writeback flush)

        public Cache(int cacheSizeBytes, int blockSizeBytes, int associativity, ReplacementPolicy replPolicy, WritePolicy writePolicy)
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

            Console.WriteLine($"Cache criada: {cacheSizeBytes} bytes, Bloco={blockSizeBytes} bytes, Linhas={numLines}, Conjuntos={numSets}, Assoc={associativity}, Repl={replPolicy}, Write={writePolicy}\n");
        }

        // Endereço de 32 bits (uint). Calcula tag, set index e offset
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

        public void Access(uint address, bool isWrite)
        {
            if (isWrite) Writes++; else Reads++;

            DecodeAddress(address, out ulong tag, out int setIndex, out int offset);
            var set = sets[setIndex];

            // Verifica se há hit
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
                            // escreve imediatamente na memória principal
                            MemoryWrites++;
                        }
                        else // Write-Back
                        {
                            line.Dirty = true;
                        }
                    }
                    Console.WriteLine($"HIT - set {setIndex}, way {i}, tag=0x{tag:X}" );
                    return;
                }
            }

            // Miss
            Misses++;
            Console.WriteLine($"MISS - set {setIndex}, tag=0x{tag:X}");

            // Tentar encontrar linha inválida
            for (int i = 0; i < set.Associativity; i++)
            {
                var line = set.Lines[i];
                if (!line.Valid)
                {
                    FillLine(line, tag, isWrite);
                    Console.WriteLine($"  Colocado em way {i} (entrada vazia)");
                    return;
                }
            }

            // Substituir uma linha (policy)
            int victim = SelectVictimLine(set);
            var victimLine = set.Lines[victim];
            // se dirty e write-back => gravar na memória
            if (victimLine.Dirty && victimLine.Valid && writePolicy == WritePolicy.WriteBack)
            {
                MemoryWrites++;
                Console.WriteLine($"  Victim way {victim} era sujo (dirty). Gravando bloco na memória principal.");
            }

            FillLine(victimLine, tag, isWrite);
            Console.WriteLine($"  Substituído way {victim}");
        }

        void FillLine(CacheBlock line, ulong tag, bool isWrite)
        {
            line.Valid = true;
            line.Tag = tag;
            line.LastUsedCounter = globalCounter++;
            line.InsertCounter = globalCounter; // marca inserção para FIFO
            line.Dirty = isWrite && writePolicy == WritePolicy.WriteBack;
            // se write-through e é escrita durante a falta, escreve na memória
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

        public void PrintStats()
        {
            Console.WriteLine("\n=== Estatísticas da Cache ===");
            Console.WriteLine($"Leituras: {Reads}");
            Console.WriteLine($"Escritas: {Writes}");
            Console.WriteLine($"Hits: {Hits}");
            Console.WriteLine($"Misses: {Misses}");
            Console.WriteLine($"Hit rate: {(Reads + Writes > 0 ? (double)Hits / (Reads + Writes) : 0):P2}");
            Console.WriteLine($"Miss rate: {(Reads + Writes > 0 ? (double)Misses / (Reads + Writes) : 0):P2}");
            Console.WriteLine($"Escritas para memória principal (Memory writes): {MemoryWrites}");
            Console.WriteLine("===========================\n");
        }
    }
}

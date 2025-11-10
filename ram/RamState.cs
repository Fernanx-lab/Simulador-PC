// Arquivo: RamState.cs
// Local sugerido na solução: ProjetoSimuladorPC/ram/RamState.cs
// Este arquivo contém implementações centrais da RAM e tipos auxiliares.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Interfaces compartilhadas do barramento - coloque este bloco em ProjetoSimuladorPC/barramento/IBus.cs se preferir
namespace ProjetoSimuladorPC.Barramento
{
    /// <summary>
    /// Interface do barramento usada por CPU / Cache / Devices para acessar memória.
    /// Coloque este arquivo em /barramento e importe onde necessário.
    /// </summary>
    public interface IBus
    {
        (byte[] data, int cycles) ReadBytes(ulong physAddr, int len);
        int WriteBytes(ulong physAddr, byte[] data);

        Task<(byte[] data, int cycles)> ReadBytesAsync(ulong physAddr, int len, CancellationToken ct = default);
        Task<int> WriteBytesAsync(ulong physAddr, byte[] data, CancellationToken ct = default);
    }

    /// <summary>
    /// Interface opcional para dispositivos mapeáveis (MMIO).
    /// Dispositivos podem implementar essa interface ou registrar handlers no RamState.
    /// </summary>
    public interface IMemoryDevice : IBus
    {
        ulong Start { get; }
        ulong Length { get; }
    }
}

// Implementações da RAM e controlador de memória
namespace ProjetoSimuladorPC.Ram
{
    using ProjetoSimuladorPC.Barramento;

    [Flags]
    public enum MemProt { None = 0, Read = 1, Write = 2, Exec = 4 }

    /// <summary>
    /// Parâmetros de tempo abstratos para a DRAM (em ciclos simulados).
    /// Coloque configurações reais no construtor do MemoryController.
    /// </summary>
    public class DramTiming
    {
        public int tCL { get; set; } = 12;   // CAS latency
        public int tRCD { get; set; } = 12;  // RAS to CAS delay
        public int tRP { get; set; } = 12;   // Row precharge time
        public int tRAS { get; set; } = 30;  // Row active time
        public int tRFC { get; set; } = 350; // Refresh cycle time
        public int tBurst { get; set; } = 4; // Burst length in cycles
    }

    /// <summary>
    /// Região mapeada na memória (ex.: CODE, HEAP, STACK, MMIO, RODATA).
    /// RamState utiliza essas regiões para checagem de permissão antes de delegar ao controlador.
    /// </summary>
    public class MemoryRegion
    {
        public ulong Start { get; }
        public ulong Size { get; }
        public MemProt Prot { get; set; }
        public string Name { get; }
        public ulong End => Start + Size - 1;

        public MemoryRegion(ulong start, ulong size, MemProt prot, string name)
        {
            Start = start;
            Size = size;
            Prot = prot;
            Name = name;
        }

        public bool Contains(ulong addr) => addr >= Start && addr <= End;
    }

    // Representa um banco físico de DRAM (rows x cols)
    internal class DramBank
    {
        private readonly byte[][] rows;
        public int OpenRow { get; private set; } = -1;
        public readonly int RowCount;
        public readonly int ColCount;

        public DramBank(int rowsCount, int colCount)
        {
            RowCount = rowsCount;
            ColCount = colCount;
            rows = new byte[rowsCount][];
            for (int i = 0; i < rowsCount; i++) rows[i] = new byte[colCount];
        }

        public bool HasOpenRow => OpenRow >= 0;

        public void ActivateRow(int row)
        {
            if (row < 0 || row >= RowCount) throw new ArgumentOutOfRangeException(nameof(row));
            OpenRow = row;
        }

        public void Precharge() => OpenRow = -1;

        public byte[] ReadFromOpenRow(int col, int len)
        {
            if (OpenRow < 0) throw new InvalidOperationException("No open row");
            if (col + len > ColCount) throw new ArgumentOutOfRangeException(nameof(len));
            var outb = new byte[len];
            Buffer.BlockCopy(rows[OpenRow], col, outb, 0, len);
            return outb;
        }

        public void WriteToOpenRow(int col, byte[] data)
        {
            if (OpenRow < 0) throw new InvalidOperationException("No open row");
            if (col + data.Length > ColCount) throw new ArgumentOutOfRangeException(nameof(data));
            Buffer.BlockCopy(data, 0, rows[OpenRow], col, data.Length);
        }

        public byte[] ReadDirect(int row, int col, int len)
        {
            if (row < 0 || row >= RowCount) throw new ArgumentOutOfRangeException(nameof(row));
            var outb = new byte[len];
            Buffer.BlockCopy(rows[row], col, outb, 0, len);
            return outb;
        }

        public void WriteDirect(int row, int col, byte[] data)
        {
            if (row < 0 || row >= RowCount) throw new ArgumentOutOfRangeException(nameof(row));
            Buffer.BlockCopy(data, 0, rows[row], col, data.Length);
        }
    }

    /// <summary>
    /// Controlador de memória: traduz endereços físicos em (channel,rank,bank,row,col)
    /// gerencia row-buffer, timings, MMIO handlers e mantém contador de ciclos.
    /// </summary>
    public class MemoryController : IBus
    {
        public readonly int Channels;
        public readonly int RanksPerChannel;
        public readonly int BanksPerRank;
        public readonly int RowsPerBank;
        public readonly int ColsPerRow; // bytes per row

        private readonly DramTiming timing;
        private readonly DramBank[,,,] banks; // [channel,rank,bank,0]
        private long cycleCounter = 0;
        private readonly ReaderWriterLockSlim memLock = new ReaderWriterLockSlim();

        public event Action<ulong, int> OnRead;
        public event Action<ulong, int> OnWrite;
        public event Action<int, int, int, int> OnRowBufferHit;
        public event Action OnRefreshStarted;

        // MMIO handlers: (start,size, readHandler, writeHandler)
        private readonly List<(ulong start, ulong size, Func<ulong, int, (byte[] data, int cycles)> readHandler, Func<ulong, byte[], int> writeHandler)> mmio
            = new List<(ulong, ulong, Func<ulong, int, (byte[], int)>, Func<ulong, byte[], int>)>();

        public MemoryController(int channels, int ranksPerChannel, int banksPerRank, int rowsPerBank, int colsPerRow, DramTiming timing = null)
        {
            Channels = channels; RanksPerChannel = ranksPerChannel; BanksPerRank = banksPerRank; RowsPerBank = rowsPerBank; ColsPerRow = colsPerRow;
            this.timing = timing ?? new DramTiming();
            banks = new DramBank[Channels, RanksPerChannel, BanksPerRank, 1];
            for (int ch = 0; ch < Channels; ch++)
                for (int r = 0; r < RanksPerChannel; r++)
                    for (int b = 0; b < BanksPerRank; b++)
                        banks[ch, r, b, 0] = new DramBank(RowsPerBank, ColsPerRow);
        }

        private void DecodeAddress(ulong physAddr, out int channel, out int rank, out int bank, out int row, out int col)
        {
            ulong addr = physAddr;
            col = (int)(addr % (ulong)ColsPerRow);
            addr /= (ulong)ColsPerRow;
            row = (int)(addr % (ulong)RowsPerBank);
            addr /= (ulong)RowsPerBank;
            bank = (int)(addr % (ulong)BanksPerRank);
            addr /= (ulong)BanksPerRank;
            rank = (int)(addr % (ulong)RanksPerChannel);
            addr /= (ulong)RanksPerChannel;
            channel = (int)(addr % (ulong)Channels);
        }

        public void RegisterMmioHandler(ulong start, ulong size, Func<ulong, int, (byte[] data, int cycles)> readHandler, Func<ulong, byte[], int> writeHandler)
        {
            mmio.Add((start, size, readHandler, writeHandler));
        }

        private (Func<ulong, int, (byte[] data, int cycles)> read, Func<ulong, byte[], int> write) FindMmio(ulong addr)
        {
            var m = mmio.FirstOrDefault(t => addr >= t.start && addr < t.start + t.size);
            return (m.readHandler, m.writeHandler);
        }

        public (byte[] data, int cycles) ReadBytes(ulong physAddr, int len)
        {
            if (len <= 0) return (Array.Empty<byte>(), 0);
            memLock.EnterWriteLock();
            try
            {
                var (mmioRead, mmioWrite) = FindMmio(physAddr);
                if (mmioRead != null)
                {
                    var r = mmioRead(physAddr, len);
                    Interlocked.Add(ref cycleCounter, r.cycles);
                    OnRead?.Invoke(physAddr, len);
                    return (r.data, r.cycles);
                }

                DecodeAddress(physAddr, out int ch, out int rk, out int bk, out int row, out int col);
                var bankObj = banks[ch, rk, bk, 0];
                int cycles = 0;

                if (bankObj.HasOpenRow && bankObj.OpenRow == row)
                {
                    cycles += timing.tCL + timing.tBurst;
                    OnRowBufferHit?.Invoke(ch, rk, bk, row);
                }
                else
                {
                    if (bankObj.HasOpenRow)
                    {
                        cycles += timing.tRP;
                        bankObj.Precharge();
                    }
                    cycles += timing.tRCD;
                    bankObj.ActivateRow(row);
                    cycles += timing.tCL + timing.tBurst;
                }

                var data = bankObj.ReadFromOpenRow(col, len);
                Interlocked.Add(ref cycleCounter, cycles);
                OnRead?.Invoke(physAddr, len);
                return (data, cycles);
            }
            finally { memLock.ExitWriteLock(); }
        }

        public int WriteBytes(ulong physAddr, byte[] data)
        {
            if (data == null || data.Length == 0) return 0;
            memLock.EnterWriteLock();
            try
            {
                var (mmioRead, mmioWrite) = FindMmio(physAddr);
                if (mmioWrite != null)
                {
                    int c = mmioWrite(physAddr, data);
                    Interlocked.Add(ref cycleCounter, c);
                    OnWrite?.Invoke(physAddr, data.Length);
                    return c;
                }

                DecodeAddress(physAddr, out int ch, out int rk, out int bk, out int row, out int col);
                var bankObj = banks[ch, rk, bk, 0];
                int cycles = 0;
                if (bankObj.HasOpenRow && bankObj.OpenRow == row)
                {
                    cycles += timing.tCL + timing.tBurst;
                    OnRowBufferHit?.Invoke(ch, rk, bk, row);
                }
                else
                {
                    if (bankObj.HasOpenRow) { cycles += timing.tRP; bankObj.Precharge(); }
                    cycles += timing.tRCD;
                    bankObj.ActivateRow(row);
                    cycles += timing.tCL + timing.tBurst;
                }

                bankObj.WriteToOpenRow(col, data);
                Interlocked.Add(ref cycleCounter, cycles);
                OnWrite?.Invoke(physAddr, data.Length);
                return cycles;
            }
            finally { memLock.ExitWriteLock(); }
        }

        private const double CycleToMs = 0.0005;

        public async Task<(byte[] data, int cycles)> ReadBytesAsync(ulong physAddr, int len, CancellationToken ct = default)
        {
            var (data, cycles) = ReadBytes(physAddr, len);
            int delayMs = (int)Math.Ceiling(cycles * CycleToMs);
            if (delayMs > 0) await Task.Delay(delayMs, ct);
            return (data, cycles);
        }

        public async Task<int> WriteBytesAsync(ulong physAddr, byte[] data, CancellationToken ct = default)
        {
            int cycles = WriteBytes(physAddr, data);
            int delayMs = (int)Math.Ceiling(cycles * CycleToMs);
            if (delayMs > 0) await Task.Delay(delayMs, ct);
            return cycles;
        }

        public int RefreshAll()
        {
            memLock.EnterWriteLock();
            try
            {
                OnRefreshStarted?.Invoke();
                for (int ch = 0; ch < Channels; ch++)
                    for (int r = 0; r < RanksPerChannel; r++)
                        for (int b = 0; b < BanksPerRank; b++)
                            banks[ch, r, b, 0].Precharge();

                Interlocked.Add(ref cycleCounter, timing.tRFC);
                return timing.tRFC;
            }
            finally { memLock.ExitWriteLock(); }
        }

        public long CurrentCycle => Interlocked.Read(ref cycleCounter);

        public string GetTopologyInfo()
        {
            return $"Channels={Channels}, Ranks={RanksPerChannel}, BanksPerRank={BanksPerRank}, RowsPerBank={RowsPerBank}, ColsPerRow={ColsPerRow}";
        }
    }

    /// <summary>
    /// RamState: fachada que mapeia regiões e valida permissões antes de delegar ao MemoryController.
    /// Arquivo sugerido: ProjetoSimuladorPC/ram/RamState.cs
    /// </summary>
    public class RamState : IBus
    {
        private readonly MemoryController memCtrl;
        private readonly List<MemoryRegion> regions = new List<MemoryRegion>();

        public event Action<ulong, int> OnRead { add { memCtrl.OnRead += value; } remove { memCtrl.OnRead -= value; } }
        public event Action<ulong, int> OnWrite { add { memCtrl.OnWrite += value; } remove { memCtrl.OnWrite -= value; } }
        public event Action<int, int, int, int> OnRowBufferHit { add { memCtrl.OnRowBufferHit += value; } remove { memCtrl.OnRowBufferHit -= value; } }
        public event Action OnRefreshStarted { add { memCtrl.OnRefreshStarted += value; } remove { memCtrl.OnRefreshStarted -= value; } }

        public RamState(MemoryController controller)
        {
            memCtrl = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        public void MapRegion(ulong start, ulong size, MemProt prot, string name)
        {
            if (start + size > ComputePhysicalSize()) throw new ArgumentOutOfRangeException(nameof(size), "Região fora do espaço físico configurado");
            var overlap = regions.Any(r => !(start + size - 1 < r.Start || r.End < start));
            if (overlap) throw new InvalidOperationException("Região sobreposta detectada");
            regions.Add(new MemoryRegion(start, size, prot, name));
        }

        public ulong ComputePhysicalSize()
        {
            ulong size = (ulong)memCtrl.Channels * (ulong)memCtrl.RanksPerChannel * (ulong)memCtrl.BanksPerRank * (ulong)memCtrl.RowsPerBank * (ulong)memCtrl.ColsPerRow;
            return size;
        }

        private MemoryRegion FindRegion(ulong addr) => regions.FirstOrDefault(r => r.Contains(addr));

        private void CheckAccessPerm(ulong addr, int len, MemProt needed)
        {
            if (addr + (ulong)len > ComputePhysicalSize()) throw new AccessViolationException($"Acesso fora do espaço físico: 0x{addr:X}");

            ulong remaining = (ulong)len;
            ulong cursor = addr;
            while (remaining > 0)
            {
                var r = FindRegion(cursor);
                if (r == null) throw new AccessViolationException($"Endereço 0x{cursor:X} não mapeado em nenhuma região");
                if ((r.Prot & needed) != needed) throw new AccessViolationException($"Violação de permissão em 0x{cursor:X} na região {r.Name}");

                ulong avail = Math.Min(remaining, r.End - cursor + 1);
                cursor += avail; remaining -= avail;
            }
        }

        public void RegisterMmioHandlers(ulong start, ulong size, Func<ulong, int, (byte[] data, int cycles)> readHandler, Func<ulong, byte[], int> writeHandler)
        {
            var region = regions.FirstOrDefault(r => r.Start == start && r.Size == size);
            if (region == null) throw new InvalidOperationException("Região não encontrada para registrar MMIO");
            memCtrl.RegisterMmioHandler(start, size, readHandler, writeHandler);
        }

        public (byte[] data, int cycles) ReadBytes(ulong physAddr, int len)
        {
            CheckAccessPerm(physAddr, len, MemProt.Read);
            return memCtrl.ReadBytes(physAddr, len);
        }

        public int WriteBytes(ulong physAddr, byte[] data)
        {
            CheckAccessPerm(physAddr, data.Length, MemProt.Write);
            return memCtrl.WriteBytes(physAddr, data);
        }

        public Task<(byte[] data, int cycles)> ReadBytesAsync(ulong physAddr, int len, CancellationToken ct = default)
            => memCtrl.ReadBytesAsync(physAddr, len, ct);

        public Task<int> WriteBytesAsync(ulong physAddr, byte[] data, CancellationToken ct = default)
            => memCtrl.WriteBytesAsync(physAddr, data, ct);

        public IEnumerable<string> GetRegionsInfo() => regions.OrderBy(r => r.Start).Select(r => $"{r.Name}: 0x{r.Start:X} - 0x{r.End:X} (size=0x{r.Size:X}, prot={r.Prot})");
        public string GetTopologyInfo() => memCtrl.GetTopologyInfo();
        public long CurrentCycle => memCtrl.CurrentCycle;
    }
}

// Exemplos minimalistas de Cache e CPU (recomendo mover para /cache e /cpu)
namespace ProjetoSimuladorPC.Cache
{
    using ProjetoSimuladorPC.Barramento;

    public class SimpleCache : IBus
    {
        private readonly IBus backend;
        private readonly Dictionary<ulong, byte> store = new Dictionary<ulong, byte>();

        public SimpleCache(IBus backend)
        {
            this.backend = backend;
        }

        public (byte[] data, int cycles) ReadBytes(ulong physAddr, int len)
        {
            var result = new byte[len];
            int totalCycles = 0;
            for (int i = 0; i < len; i++)
            {
                ulong a = physAddr + (ulong)i;
                if (store.TryGetValue(a, out byte v)) { result[i] = v; }
                else
                {
                    var (d, c) = backend.ReadBytes(a, 1);
                    result[i] = d[0];
                    store[a] = d[0];
                    totalCycles += c;
                }
            }
            return (result, totalCycles);
        }

        public int WriteBytes(ulong physAddr, byte[] data)
        {
            int total = 0;
            for (int i = 0; i < data.Length; i++)
            {
                ulong a = physAddr + (ulong)i;
                store[a] = data[i];
                total += backend.WriteBytes(a, new byte[] { data[i] });
            }
            return total;
        }

        public Task<(byte[] data, int cycles)> ReadBytesAsync(ulong physAddr, int len, CancellationToken ct = default)
            => Task.FromResult(ReadBytes(physAddr, len));

        public Task<int> WriteBytesAsync(ulong physAddr, byte[] data, CancellationToken ct = default)
            => Task.FromResult(WriteBytes(physAddr, data));
    }
}

namespace ProjetoSimuladorPC.Cpu
{
    using ProjetoSimuladorPC.Barramento;

    public class CpuSimple
    {
        private readonly IBus bus;
        public CpuSimple(IBus bus) { this.bus = bus; }

        public (byte[] data, int cycles) Load(ulong address, int len)
        {
            return bus.ReadBytes(address, len);
        }

        public int Store(ulong address, byte[] data)
        {
            return bus.WriteBytes(address, data);
        }
    }
}

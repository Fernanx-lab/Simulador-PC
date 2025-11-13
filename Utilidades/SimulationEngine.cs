using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ProjetoSimIO.Core;
using ProjetoSimuladorPC.Cache;
using ProjetoSimuladorPC.Cpu;
using ProjetoSimuladorPC.DMA;
using ProjetoSimuladorPC.RAM;

namespace ProjetoSimuladorPC.Utilidades;

/// <summary>
/// Motor de simulação que interpõe e conecta os módulos (CPU, RAM, Cache, DMA, PIC, Metrics)
/// usando as States já definidas (fachadas). Projetado para ser registrado como singleton.
/// </summary>
public class SimulationEngine : IDisposable
{
    readonly SimulationState simState;
    readonly Metrics metrics;
    readonly PicController pic;
    readonly CpuSimulator cpuSimulator;
    readonly Cache.Cache cacheSim;
    readonly DispositivoMMIO mmio;
    readonly DMA.DMA dmaSim;
    readonly RamState ram;
    readonly DmaState dmaState;
    System.Threading.Timer? autoTimer;

    // Handlers nomeados para subscribe/unsubscribe corretos
    readonly EventHandler<ProjetoSimuladorPC.RAM.MemoryChangedEventArgs> ramMemoryChangedHandler;
    readonly EventHandler dmaStateChangedHandler;

    public SimulationEngine(SimulationState simulationState)
    {
        simState = simulationState ?? throw new ArgumentNullException(nameof(simulationState));

        // reutiliza as fachadas existentes no SimulationState para manter referências visíveis ao UI
        ram = simState.Ram;
        dmaState = simState.Dma;

        // métricas e PIC simples
        metrics = new Metrics();
        pic = new PicController();

        // CPU: passa a CpuState de simState para que a UI veja as mudanças diretamente
        var cpuState = simState.Cpu;
        // Modificado: passa cpuState compartilhada ao criar o CpuSimulator
        cpuSimulator = new CpuSimulator(ram, pic, metrics, cpuState);

        // MMIO e DMA: mmio com faixa baseada no Config (fallback)
        uint dmaBase = simState.Config?.DmaBase ?? 0x1000_0200;
        mmio = new DispositivoMMIO((int)dmaBase, (int)(dmaBase + 0xFF)); // faixa simples
        dmaSim = new DMA.DMA(ram, mmio, dmaState);

        // Cache: cria uma instância de Cache ligada à fachada CacheState do SimulationState
        var cacheState = simState.Cache;
        int cacheSizeBytes = ParseMemorySize(simState.Config?.L1Size) ?? 16 * 1024;
        int blockSize = Math.Max(1, simState.Config?.L1LineSize ?? 64);
        int assoc = Math.Max(1, simState.Config?.L1Assoc ?? 2);
        var wp = (simState.Config?.L1WritePolicy ?? "WT").ToUpperInvariant() == "WT" ? WritePolicy.WriteThrough : WritePolicy.WriteBack;
        // substituição fixa LRU para simplificação
        cacheSim = new Cache.Cache(cacheSizeBytes, blockSize, assoc, ReplacementPolicy.LRU, wp, cacheState);

        // ANEXA a cache à RAM para que acessos reais atualizem estatísticas
        ram.AttachCache(cacheSim);

        // cria handlers nomeados que notificam o SimulationState
        ramMemoryChangedHandler = (_, __) => simState.NotifyStateChanged();
        dmaStateChangedHandler = (_, __) => simState.NotifyStateChanged();

        // Subscrições para propagar mudanças à UI (usando handlers nomeados)
        ram.MemoryChanged += ramMemoryChangedHandler;
        dmaState.StateChanged += dmaStateChangedHandler;
        // cacheSim atualiza a fachada por si só; apenas garanta notificação quando engine faz avanços

        // Exponha as instâncias (garante que SimulationState referencia as fachadas corretas)
        simState.Ram = ram;
        simState.Cache = cacheState;
        simState.Dma = dmaState;
    }

    /// <summary>
    /// Avança a simulação um ciclo (tick) — executa lógica da CPU e notifica UI.
    /// </summary>
    public void AdvanceOneCycle()
    {
        // CPU executa instrução / trata IRQs
        cpuSimulator.Tick();

        // opcional: atualizar contadores da cache na fachada (forçar sync)
        cacheSim.UpdateState();

        // incrementa ciclo global e notifica UI
        simState.AdvanceCycle(1);
        simState.NotifyStateChanged();
    }

    /// <summary>
    /// Inicia transferência DMA assincronamente.
    /// </summary>
    public Task StartDmaAsync(int origem, int destino, int tamanho, int delayMs = 10)
    {
        return dmaSim.ExecutarTransferenciaAsync(origem, destino, tamanho, delayMs);
    }

    /// <summary>
    /// Inicia modo automático que chama AdvanceOneCycle em intervalo.
    /// </summary>
    public void StartAuto(int intervalMs)
    {
        StopAuto();
        autoTimer = new System.Threading.Timer(_ =>
        {
            try { AdvanceOneCycle(); }
            catch { }
        }, null, 0, Math.Max(1, intervalMs));
    }

    public void StopAuto()
    {
        autoTimer?.Dispose();
        autoTimer = null;
    }

    static int? ParseMemorySize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim().ToUpperInvariant();
        if (s.EndsWith("KB"))
        {
            if (int.TryParse(s[..^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v * 1024;
        }
        else if (s.EndsWith("MB"))
        {
            if (int.TryParse(s[..^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v * 1024 * 1024;
        }
        else if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
        {
            return v;
        }
        return null;
    }

    public void Dispose()
    {
        StopAuto();
        // unsubscribes corretos usando os mesmos handlers registrados
        try { ram.MemoryChanged -= ramMemoryChangedHandler; } catch { }
        try { dmaState.StateChanged -= dmaStateChangedHandler; } catch { }
    }
}
